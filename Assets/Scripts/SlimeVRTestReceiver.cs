using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

/// <summary>
/// SlimeVR の OSC 出力を受信し、ダミーキャラクターのボーンへ同期するレシーバー。
///
/// 対応フォーマット:
///  - SlimeVR「VRChat OSC」出力:  /tracking/trackers/{1..8}/position , /rotation (各 float×3, Unity座標/オイラー角[度])
///                                 /tracking/trackers/head/position , /rotation
///    → 部位名ではなく番号で届くため、キャリブレーション時に「高さ」と「左右」から自動で部位を割り当てる
///  - 部位名入りアドレス (/.../chest/..., /.../leftfoot/... 等。SlimeVRMockSender もこの形式)
///  - VMC (/VMC/Ext/Bone/Pos)
///  - OSC Bundle (#bundle) の再帰展開
///
/// スレッド設計:
///  - 受信スレッドは「パース → キュー投入」だけを行い、Unity API (Time.time 等) は一切呼ばない
///  - ボーン更新・時刻記録はすべてメインスレッド (Update) で行う
///
/// 前提の装着構成 (計7点・頭なし): 胸1, 両手(握りこみ)2, 両膝2, 両足首2
/// </summary>
public class SlimeVRTestReceiver : MonoBehaviour
{
    public enum Role { Chest = 0, LeftHand, RightHand, LeftKnee, RightKnee, LeftFoot, RightFoot, Head }
    private const int RoleCount = 8;
    private static readonly string[] RoleLabels = { "胸", "左手", "右手", "左膝", "右膝", "左足", "右足", "頭" };

    [Header("通信設定")]
    [Tooltip("SlimeVR の VRChat OSC「ポートアウト」と同じ番号にする (既定 9000)")]
    public int port = 9000;

    [Header("キャラクターのボーン（関節）割り当て（7点対応）")]
    [Tooltip("胸・胴体のTransform (1点)")]
    public Transform chestBone;
    [Tooltip("左手・左腕のTransform (1点: 握りこみ)")]
    public Transform leftArmBone;
    [Tooltip("右手・右腕のTransform (1点: 握りこみ)")]
    public Transform rightArmBone;
    [Tooltip("左膝のTransform (1点)")]
    public Transform leftKneeBone;
    [Tooltip("右膝のTransform (1点)")]
    public Transform rightKneeBone;
    [Tooltip("左足首のTransform (1点)")]
    public Transform leftFootBone;
    [Tooltip("右足首のTransform (1点)")]
    public Transform rightFootBone;
    [Tooltip("頭のTransform (任意・未装着でも動作可能)")]
    public Transform headBone;

    [Header("トラッカー番号の割り当て (/tracking/trackers/N/)")]
    [Tooltip("ON: キャリブレーション時に高さと左右から自動割り当て。OFF: 下の番号を使用")]
    public bool autoAssignTrackerIds = true;
    [Tooltip("手動割り当て時の番号 (-1 = 未使用)。順番: 胸, 左手, 右手, 左膝, 右膝, 左足, 右足")]
    public int[] manualTrackerIds = { -1, -1, -1, -1, -1, -1, -1 };

    [Header("トラッキング補正")]
    [Tooltip("胸の上下動 (しゃがみ) を人形に反映する倍率")]
    public float positionMultiplier = 1.0f;
    [Tooltip("手の位置から腕の向きを計算する (握りこみトラッカー向け)")]
    public bool usePositionForArms = true;
    [Tooltip("左右が鏡写しになる場合は 180 を入れる")]
    public float facingYawOffset = 0f;
    [Tooltip("胸トラッカーから見た肩の位置 (m)。X=左右幅, Y=高さ")]
    public Vector2 shoulderOffsetFromChest = new Vector2(0.18f, 0.15f);
    [Tooltip("この秒数データが来なければその部位は停止扱い (秒)")]
    public float trackerTimeout = 3.0f;
    [Tooltip("データ受信開始からこの秒数後に自動キャリブレーション (0以下で無効)")]
    public float autoCalibrateDelay = 2.5f;

    [Header("デバッグ表示")]
    public bool showDebugLog = false;
    [Tooltip("画面左下に受信モニターを表示")]
    public bool showMonitorHUD = true;

    // ---- 統計 (受信スレッドは Interlocked でカウントのみ) ----
    private int packetCountThreadSafe = 0;
    private int lastSeenPacketCount = 0;
    public int totalPacketsReceived = 0;
    public int totalMessagesProcessed = 0;
    public float lastPacketTime = -999f;
    private float firstPacketTime = -1f;
    private string bindError = null;

    // ---- トラッカー状態 ----
    private class TrackerState
    {
        public Vector3 pos;
        public Quaternion rot = Quaternion.identity;
        public bool hasPos, hasRot;
        public float lastTime = -999f;
        public Quaternion calibRot = Quaternion.identity;
    }
    private readonly TrackerState[] roles = new TrackerState[RoleCount];
    private readonly Dictionary<string, TrackerState> idTrackers = new Dictionary<string, TrackerState>();
    private readonly Dictionary<string, Role> idToRole = new Dictionary<string, Role>();

    // ---- キャリブレーション ----
    public bool trackingCalibrated = false;
    private Quaternion facingYaw = Quaternion.identity;
    private float calibChestY = 0f;
    private float lastAutoCalibAttempt = -999f;

    // ---- ボーンのレスト姿勢 (ルート空間) ----
    private Transform rigRoot;
    private readonly Quaternion[] restRot = new Quaternion[RoleCount];
    private Vector3 chestRestLocalPos;
    private bool restRecorded = false;

    // ---- 受信スレッド ----
    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool isRunning = false;
    private static UdpClient s_activeClient; // 再生停止し損ねたソケットの後始末用

    private readonly Queue<ParsedOscData> messageQueue = new Queue<ParsedOscData>();
    private readonly object queueLock = new object();

    // ---- HUD用: 最近受信したアドレス ----
    private readonly Dictionary<string, string> recentAddresses = new Dictionary<string, string>();
    private readonly Dictionary<string, float> recentAddressTimes = new Dictionary<string, float>();

    public struct ParsedOscData
    {
        public string address;
        public List<float> floats;
        public List<string> strings;
    }

    // =====================================================================
    // Unity ライフサイクル
    // =====================================================================
    void Awake()
    {
        for (int i = 0; i < RoleCount; i++) roles[i] = new TrackerState();
    }

    void Start()
    {
        AutoFindBonesIfEmpty();
        RecordRestPose();
        StartReceiving();
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopReceiving;
#endif
    }

    void OnDisable() { StopReceiving(); }
    void OnDestroy()
    {
        StopReceiving();
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopReceiving;
#endif
    }
    void OnApplicationQuit() { StopReceiving(); }

    public void AutoFindBonesIfEmpty()
    {
        PlayerDummy dummy = FindObjectOfType<PlayerDummy>();
        if (dummy != null)
        {
            if (chestBone == null) chestBone = dummy.spine;
            if (leftArmBone == null) leftArmBone = dummy.leftArmBone;
            if (rightArmBone == null) rightArmBone = dummy.rightArmBone;
            if (leftKneeBone == null) leftKneeBone = dummy.leftKneeBone;
            if (rightKneeBone == null) rightKneeBone = dummy.rightKneeBone;
            if (leftFootBone == null) leftFootBone = dummy.leftFootBone;
            if (rightFootBone == null) rightFootBone = dummy.rightFootBone;
            if (headBone == null) headBone = dummy.headBone;
            rigRoot = dummy.transform;
        }
        if (rigRoot == null && chestBone != null) rigRoot = chestBone.parent;
    }

    private Transform BoneOf(Role r)
    {
        switch (r)
        {
            case Role.Chest: return chestBone;
            case Role.LeftHand: return leftArmBone;
            case Role.RightHand: return rightArmBone;
            case Role.LeftKnee: return leftKneeBone;
            case Role.RightKnee: return rightKneeBone;
            case Role.LeftFoot: return leftFootBone;
            case Role.RightFoot: return rightFootBone;
            case Role.Head: return headBone;
        }
        return null;
    }

    private Quaternion RootRot => rigRoot != null ? rigRoot.rotation : Quaternion.identity;

    private void RecordRestPose()
    {
        Quaternion invRoot = Quaternion.Inverse(RootRot);
        for (int i = 0; i < RoleCount; i++)
        {
            Transform b = BoneOf((Role)i);
            restRot[i] = b != null ? invRoot * b.rotation : Quaternion.identity;
        }
        if (chestBone != null) chestRestLocalPos = chestBone.localPosition;
        restRecorded = true;
    }

    void Update()
    {
        if (!restRecorded) RecordRestPose();

        // 受信スレッドのカウンタをメインスレッドで時刻に変換
        int count = Interlocked.CompareExchange(ref packetCountThreadSafe, 0, 0);
        if (count != lastSeenPacketCount)
        {
            lastSeenPacketCount = count;
            totalPacketsReceived = count;
            lastPacketTime = Time.time;
            if (firstPacketTime < 0f) firstPacketTime = Time.time;
        }

        // キューを吸い出してからロック外で処理
        List<ParsedOscData> batch = null;
        lock (queueLock)
        {
            if (messageQueue.Count > 0)
            {
                batch = new List<ParsedOscData>(messageQueue);
                messageQueue.Clear();
            }
        }
        if (batch != null)
        {
            foreach (var d in batch) ProcessParsedData(d);
        }

        CopyIdTrackersToRoles();

        // 自動キャリブレーション（受信開始後、直立姿勢が整ったタイミングで実行。失敗時は1秒おきにリトライ）
        if (!trackingCalibrated && autoCalibrateDelay > 0f && firstPacketTime >= 0f
            && Time.time - firstPacketTime >= autoCalibrateDelay
            && Time.time - lastAutoCalibAttempt >= 1.0f
            && HasAnyPositionData())
        {
            lastAutoCalibAttempt = Time.time;
            CalibrateTracking();
        }

        ApplyToRig();
    }

    // =====================================================================
    // メッセージ解釈 (メインスレッド)
    // =====================================================================
    private void ProcessParsedData(ParsedOscData data)
    {
        totalMessagesProcessed++;
        float now = Time.time;
        RememberAddress(data, now);

        string addr = data.address.ToLowerInvariant();

        // --- 1. SlimeVR VRChat OSC 形式: /tracking/trackers/{id}/position|rotation ---
        string[] parts = addr.Split('/');
        if (parts.Length >= 5 && parts[1] == "tracking" && parts[2] == "trackers" && data.floats.Count >= 3)
        {
            string id = parts[3];
            string kind = parts[4];
            TrackerState st;
            if (id == "head")
            {
                st = roles[(int)Role.Head];
            }
            else
            {
                if (!idTrackers.TryGetValue(id, out st))
                {
                    st = new TrackerState();
                    idTrackers[id] = st;
                }
            }
            Vector3 v = new Vector3(data.floats[0], data.floats[1], data.floats[2]);
            if (kind.StartsWith("pos")) { st.pos = v; st.hasPos = true; }
            else if (kind.StartsWith("rot")) { st.rot = Quaternion.Euler(v); st.hasRot = true; } // VRChat仕様: Unityのオイラー角(度)
            st.lastTime = now;
            return;
        }

        // --- 2. VMC 形式: /VMC/Ext/Bone/Pos (name, px,py,pz, qx,qy,qz,qw) ※Unity座標系 ---
        if (addr.StartsWith("/vmc/ext/bone/pos") && data.strings.Count > 0 && data.floats.Count >= 7)
        {
            Role? r = RoleFromVmcBone(data.strings[0].ToLowerInvariant());
            if (r.HasValue)
            {
                TrackerState st = roles[(int)r.Value];
                st.rot = new Quaternion(data.floats[3], data.floats[4], data.floats[5], data.floats[6]);
                st.hasRot = true;
                st.lastTime = now;
            }
            return;
        }

        // --- 3. 部位名入りアドレス (MockSender 等) ---
        if (data.floats.Count == 0) return;
        Role? role = RoleFromName(addr);
        if (!role.HasValue) return;

        TrackerState s = roles[(int)role.Value];
        int n = data.floats.Count;
        if (n >= 7)
        {
            s.pos = new Vector3(data.floats[0], data.floats[1], data.floats[2]); s.hasPos = true;
            s.rot = new Quaternion(data.floats[3], data.floats[4], data.floats[5], data.floats[6]); s.hasRot = true;
        }
        else if (n == 6)
        {
            s.pos = new Vector3(data.floats[0], data.floats[1], data.floats[2]); s.hasPos = true;
            s.rot = Quaternion.Euler(data.floats[3], data.floats[4], data.floats[5]); s.hasRot = true;
        }
        else if (n == 4)
        {
            s.rot = new Quaternion(data.floats[0], data.floats[1], data.floats[2], data.floats[3]); s.hasRot = true;
        }
        else if (n == 3)
        {
            Vector3 v = new Vector3(data.floats[0], data.floats[1], data.floats[2]);
            if (addr.Contains("position") || addr.EndsWith("/pos")) { s.pos = v; s.hasPos = true; }
            else { s.rot = Quaternion.Euler(v); s.hasRot = true; }
        }
        s.lastTime = now;

        if (showDebugLog) Debug.Log($"[OSC] {data.address} -> {role.Value}");
    }

    private static Role? RoleFromName(string addr)
    {
        bool left = addr.Contains("left");
        bool right = addr.Contains("right");
        if (addr.Contains("chest") || addr.Contains("waist") || addr.Contains("spine") || addr.Contains("hip")) return Role.Chest;
        if (addr.Contains("knee")) return left ? Role.LeftKnee : right ? Role.RightKnee : (Role?)null;
        if (addr.Contains("foot") || addr.Contains("ankle")) return left ? Role.LeftFoot : right ? Role.RightFoot : (Role?)null;
        if (addr.Contains("hand") || addr.Contains("controller") || addr.Contains("arm")) return left ? Role.LeftHand : right ? Role.RightHand : (Role?)null;
        if (addr.Contains("head")) return Role.Head;
        return null;
    }

    private static Role? RoleFromVmcBone(string b)
    {
        if (b == "chest" || b == "upperchest" || b == "spine") return Role.Chest;
        if (b == "leftupperarm") return Role.LeftHand;
        if (b == "rightupperarm") return Role.RightHand;
        if (b == "leftupperleg") return Role.LeftKnee;
        if (b == "rightupperleg") return Role.RightKnee;
        if (b == "leftlowerleg") return Role.LeftFoot;
        if (b == "rightlowerleg") return Role.RightFoot;
        if (b == "head") return Role.Head;
        return null;
    }

    private void CopyIdTrackersToRoles()
    {
        foreach (var kv in idToRole)
        {
            if (!idTrackers.TryGetValue(kv.Key, out TrackerState src)) continue;
            TrackerState dst = roles[(int)kv.Value];
            dst.pos = src.pos; dst.hasPos = src.hasPos;
            dst.rot = src.rot; dst.hasRot = src.hasRot;
            dst.lastTime = src.lastTime;
        }
    }

    private bool Recent(TrackerState s) => Time.time - s.lastTime < trackerTimeout;

    private bool HasAnyPositionData()
    {
        foreach (var kv in idTrackers) if (kv.Value.hasPos && Recent(kv.Value)) return true;
        return roles[(int)Role.Chest].hasPos && Recent(roles[(int)Role.Chest]);
    }

    /// <summary>
    /// 胸の「足からの高さ」。SlimeVR (HMDなし) は頭を原点(0,0,0)にした相対位置で送ってくるため、
    /// 胸の絶対Yはしゃがんでも変わらない。代わりに足が上がってくるので、胸Y - 低い方の足Y で測る。
    /// </summary>
    private float ChestHeightAboveFeet()
    {
        TrackerState chest = roles[(int)Role.Chest];
        TrackerState lf = roles[(int)Role.LeftFoot];
        TrackerState rf = roles[(int)Role.RightFoot];
        bool hasL = lf.hasPos && Recent(lf);
        bool hasR = rf.hasPos && Recent(rf);
        if (!hasL && !hasR) return chest.pos.y;
        float footY = hasL && hasR ? Mathf.Min(lf.pos.y, rf.pos.y) : (hasL ? lf.pos.y : rf.pos.y);
        return chest.pos.y - footY;
    }

    // =====================================================================
    // キャリブレーション
    // =====================================================================
    /// <summary>
    /// 直立・腕を下ろした姿勢で呼ぶ。トラッカー番号の自動割り当て、正面方向、各部位の基準回転を記録する。
    /// PlayerDummy.Calibrate() から呼ばれる。
    /// </summary>
    public void CalibrateTracking()
    {
        if (!restRecorded) { AutoFindBonesIfEmpty(); RecordRestPose(); }
        if (autoAssignTrackerIds) AutoAssignTrackerIds();
        else ApplyManualTrackerIds();
        CopyIdTrackersToRoles();

        TrackerState chest = roles[(int)Role.Chest];
        if (!chest.hasPos && !chest.hasRot && idTrackers.Count == 0)
        {
            Debug.LogWarning("[SlimeVR Receiver] トラッカーデータが届いていないためキャリブレーションできません。");
            return;
        }

        facingYaw = (chest.hasRot ? YawOnly(chest.rot) : Quaternion.identity) * Quaternion.Euler(0f, facingYawOffset, 0f);
        if (chest.hasPos) calibChestY = ChestHeightAboveFeet();

        for (int i = 0; i < RoleCount; i++)
        {
            if (roles[i].hasRot) roles[i].calibRot = roles[i].rot;
        }

        trackingCalibrated = true;
        ApplyToRig(); // 人形をレスト姿勢に戻した状態で PlayerDummy に基準を記録させる
        Debug.Log($"[SlimeVR Receiver] キャリブレーション完了: 割り当て {DescribeMapping()}");
    }

    private static Quaternion YawOnly(Quaternion q)
    {
        Vector3 f = q * Vector3.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) return Quaternion.identity;
        return Quaternion.LookRotation(f.normalized, Vector3.up);
    }

    private void ApplyManualTrackerIds()
    {
        idToRole.Clear();
        for (int i = 0; i < manualTrackerIds.Length && i < 7; i++)
        {
            if (manualTrackerIds[i] >= 0) idToRole[manualTrackerIds[i].ToString()] = (Role)i;
        }
    }

    /// <summary>
    /// 番号で届くトラッカーを、高さ（胸=最上段 / 手=中段上 / 膝・足=下段）と
    /// 左右位置で自動的に部位へ割り当てる。
    /// </summary>
    private void AutoAssignTrackerIds()
    {
        var list = new List<KeyValuePair<string, TrackerState>>();
        foreach (var kv in idTrackers)
        {
            if (kv.Value.hasPos && (Recent(kv.Value) || kv.Value.pos.sqrMagnitude > 0.001f))
            {
                list.Add(kv);
            }
        }
        if (list.Count == 0) return;

        idToRole.Clear();
        list.Sort((a, b) => b.Value.pos.y.CompareTo(a.Value.pos.y)); // 高い順 (Y大→小)

        // 1. 最も高い位置にあるトラッカーを「胸」に割り当て
        var chest = list[0];
        idToRole[chest.Key] = Role.Chest;
        Quaternion yaw = (chest.Value.hasRot ? YawOnly(chest.Value.rot) : Quaternion.identity) * Quaternion.Euler(0f, facingYawOffset, 0f);
        Quaternion yawInv = Quaternion.Inverse(yaw);

        float floorY = list[list.Count - 1].Value.pos.y;
        float h = Mathf.Max(0.3f, chest.Value.pos.y - floorY);

        var hands = new List<KeyValuePair<string, float>>();
        var legs = new List<KeyValuePair<string, float>>();

        for (int i = 1; i < list.Count; i++)
        {
            Vector3 p = list[i].Value.pos;
            float t = (p.y - floorY) / h;                       // 0=最下段, 1=胸
            float lateral = (yawInv * (p - chest.Value.pos)).x; // -:左, +:右
            var item = new KeyValuePair<string, float>(list[i].Key, lateral);

            if (t < 0.45f)
            {
                legs.Add(item); // 下半分は脚（膝または足）
            }
            else
            {
                // 上半分: 体の中心線(横幅10cm以内)かつ胸より下なら「腰」、それ以外は「手/肘」
                if (Mathf.Abs(lateral) < 0.08f && list.Count >= 6)
                {
                    // 腰トラッカー（判定には直接使わないが認識はしておく）
                    continue;
                }
                hands.Add(item);
            }
        }

        // 手の左右割り当て
        AssignPair(hands, Role.LeftHand, Role.RightHand);

        // 脚の割り当て: 4個あれば膝と足、2個なら足（および膝）に割り当て
        if (legs.Count >= 4)
        {
            legs.Sort((a, b) => idTrackers[a.Key].pos.y.CompareTo(idTrackers[b.Key].pos.y)); // 低い順
            var feet = new List<KeyValuePair<string, float>> { legs[0], legs[1] };
            var knees = new List<KeyValuePair<string, float>> { legs[2], legs[3] };
            AssignPair(feet, Role.LeftFoot, Role.RightFoot);
            AssignPair(knees, Role.LeftKnee, Role.RightKnee);
        }
        else
        {
            // 2個の場合は足首に割り当て（膝未装着構成でも足首の曲がりで検出可能）
            AssignPair(legs, Role.LeftFoot, Role.RightFoot);
            // 膝にも同じ番号を割り当ててフォールバック
            if (idToRole.ContainsKey(legs[0].Key)) idToRole[legs[0].Key] = Role.LeftFoot;
        }
    }

    private void AssignPair(List<KeyValuePair<string, float>> items, Role leftRole, Role rightRole)
    {
        if (items.Count == 0) return;
        items.Sort((a, b) => a.Value.CompareTo(b.Value));
        if (items.Count == 1)
        {
            idToRole[items[0].Key] = items[0].Value < 0f ? leftRole : rightRole;
            return;
        }
        idToRole[items[0].Key] = leftRole;
        idToRole[items[items.Count - 1].Key] = rightRole;
    }

    private string DescribeMapping()
    {
        if (idToRole.Count == 0) return "(番号割り当てなし)";
        var sb = new StringBuilder();
        foreach (var kv in idToRole) sb.Append($"#{kv.Key}={RoleLabels[(int)kv.Value]} ");
        return sb.ToString();
    }

    // =====================================================================
    // ボーンへの反映 (メインスレッド・毎フレーム)
    // =====================================================================
    private Quaternion ToRigSpace(Quaternion worldDelta)
    {
        Quaternion inv = Quaternion.Inverse(facingYaw);
        return inv * worldDelta * facingYaw;
    }

    private void ApplyRotation(Role r)
    {
        Transform bone = BoneOf(r);
        TrackerState s = roles[(int)r];
        if (bone == null || !s.hasRot || !Recent(s)) return;
        Quaternion delta = s.rot * Quaternion.Inverse(s.calibRot);
        bone.rotation = RootRot * ToRigSpace(delta) * restRot[(int)r];
    }

    private void ApplyArm(Role r, Vector3 restDir, float shoulderSide)
    {
        Transform bone = BoneOf(r);
        TrackerState hand = roles[(int)r];
        TrackerState chest = roles[(int)Role.Chest];
        if (bone == null || !Recent(hand)) return;

        if (usePositionForArms && hand.hasPos && chest.hasPos && Recent(chest))
        {
            Vector3 rel = Quaternion.Inverse(facingYaw) * (hand.pos - chest.pos);
            Vector3 shoulder = new Vector3(shoulderSide * shoulderOffsetFromChest.x, shoulderOffsetFromChest.y, 0f);
            Vector3 dir = rel - shoulder;
            if (dir.sqrMagnitude > 0.0025f)
            {
                bone.rotation = RootRot * Quaternion.FromToRotation(restDir, dir.normalized) * restRot[(int)r];
                return;
            }
        }
        ApplyRotation(r);
    }

    private void ApplyToRig()
    {
        if (!restRecorded) return;
        // 胸: 回転 + 上下動 (しゃがみ)
        TrackerState chest = roles[(int)Role.Chest];
        if (chestBone != null && Recent(chest))
        {
            ApplyRotation(Role.Chest);
            if (chest.hasPos && trackingCalibrated)
            {
                float dy = (ChestHeightAboveFeet() - calibChestY) * positionMultiplier;
                chestBone.localPosition = chestRestLocalPos + Vector3.up * dy;
            }
        }

        // 両手: 胸から見た手の位置で腕の向きを決める
        ApplyArm(Role.LeftHand, Vector3.left, -1f);
        ApplyArm(Role.RightHand, Vector3.right, 1f);

        // 膝・足首・頭: キャリブレーション時からの回転差分
        ApplyRotation(Role.LeftKnee);
        ApplyRotation(Role.RightKnee);
        ApplyRotation(Role.LeftFoot);
        ApplyRotation(Role.RightFoot);
        ApplyRotation(Role.Head);
    }

    // =====================================================================
    // UDP 受信 (バックグラウンドスレッド: Unity API 呼び出し禁止)
    // =====================================================================
    private void StartReceiving()
    {
        StopReceiving();

        if (s_activeClient != null)
        {
            try { s_activeClient.Close(); } catch { }
            s_activeClient = null;
        }

        try
        {
            udpClient = new UdpClient();
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // Windows UDPの10054 (ICMP Connection Reset) による受信停止を防止
            const int SIO_UDP_CONNRESET = -1744830452;
            try { udpClient.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null); } catch { }

            try
            {
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            }
            catch
            {
                // Any がダメなら Loopback (127.0.0.1) にフォールバック
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
            }

            s_activeClient = udpClient;
            bindError = null;

            isRunning = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "SlimeVR OSC Receiver" };
            receiveThread.Start();
            Debug.Log($"[SlimeVR Receiver] ポート {port} で受信待機を開始しました。");
        }
        catch (System.Exception e)
        {
            bindError = e.Message;
            Debug.LogError($"[SlimeVR Receiver] ポート {port} のバインドに失敗しました。\n{e.Message}\n" +
                           "→ 他のアプリが同じポートを使っていないか確認してください。");
            try { udpClient?.Close(); } catch { }
            udpClient = null;
        }
    }

    private void StopReceiving()
    {
        isRunning = false;
        if (udpClient != null)
        {
            try { udpClient.Close(); } catch { }
            if (s_activeClient == udpClient) s_activeClient = null;
            udpClient = null;
        }
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(300);
        receiveThread = null;
    }

    private void ReceiveLoop()
    {
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
        UdpClient client = udpClient;
        while (isRunning && client != null)
        {
            try
            {
                byte[] data = client.Receive(ref anyIP);
                Interlocked.Increment(ref packetCountThreadSafe);
                ParseIncomingPacket(data, 0, data.Length);
            }
            catch (SocketException)
            {
                if (!isRunning) break;
                Thread.Sleep(5); // 一時的なソケットエラー時はリトライ
            }
            catch (System.ObjectDisposedException) { break; }
            catch (System.Exception) { /* 不正パケットは無視して受信継続 */ }
        }
    }

    private void ParseIncomingPacket(byte[] data, int offset, int length)
    {
        if (data == null || length < 4 || offset + length > data.Length) return;

        if (length >= 16 && data[offset] == (byte)'#'
            && Encoding.ASCII.GetString(data, offset, 7) == "#bundle")
        {
            // "#bundle\0"(8) + TimeTag(8) + [size(4) + element]...
            int index = offset + 16;
            int end = offset + length;
            while (index + 4 <= end)
            {
                int size = ReadInt32BigEndian(data, ref index, end);
                if (size <= 0 || index + size > end) break;
                ParseIncomingPacket(data, index, size);
                index += size;
            }
            return;
        }

        ParseOscMessage(data, offset, length);
    }

    private void ParseOscMessage(byte[] data, int offset, int length)
    {
        int index = offset;
        int end = offset + length;

        string address = ReadOscString(data, ref index, end);
        if (string.IsNullOrEmpty(address) || address[0] != '/') return;

        string typeTag = ReadOscString(data, ref index, end);
        if (string.IsNullOrEmpty(typeTag) || typeTag[0] != ',') return;

        var floats = new List<float>();
        var strings = new List<string>();
        for (int i = 1; i < typeTag.Length; i++)
        {
            switch (typeTag[i])
            {
                case 'f': floats.Add(ReadFloatBigEndian(data, ref index, end)); break;
                case 'i': floats.Add(ReadInt32BigEndian(data, ref index, end)); break;
                case 's': strings.Add(ReadOscString(data, ref index, end)); break;
                case 'd':
                    if (index + 8 <= end)
                    {
                        byte[] b = new byte[8];
                        for (int k = 0; k < 8; k++) b[k] = data[index + 7 - k];
                        floats.Add((float)System.BitConverter.ToDouble(b, 0));
                    }
                    index += 8;
                    break;
                case 'T': floats.Add(1f); break;
                case 'F': floats.Add(0f); break;
                case 'N': case 'I': break;
                default: index += 4; break;
            }
        }

        lock (queueLock)
        {
            // メインスレッドが止まっている間に溜まり過ぎないよう上限を設ける
            if (messageQueue.Count > 4000) messageQueue.Clear();
            messageQueue.Enqueue(new ParsedOscData { address = address, floats = floats, strings = strings });
        }
    }

    private static string ReadOscString(byte[] data, ref int index, int end)
    {
        if (index >= end) return string.Empty;
        int start = index;
        while (index < end && data[index] != 0) index++;
        string s = Encoding.ASCII.GetString(data, start, index - start);
        index++; // null終端
        index += (4 - ((index - start) % 4)) % 4; // 4バイト境界
        return s;
    }

    private static float ReadFloatBigEndian(byte[] data, ref int index, int end)
    {
        if (index + 4 > end) { index += 4; return 0f; }
        byte[] b = { data[index + 3], data[index + 2], data[index + 1], data[index] };
        index += 4;
        return System.BitConverter.ToSingle(b, 0);
    }

    private static int ReadInt32BigEndian(byte[] data, ref int index, int end)
    {
        if (index + 4 > end) { index += 4; return 0; }
        int v = (data[index] << 24) | (data[index + 1] << 16) | (data[index + 2] << 8) | data[index + 3];
        index += 4;
        return v;
    }

    // =====================================================================
    // 受信モニター HUD
    // =====================================================================
    private void RememberAddress(ParsedOscData d, float now)
    {
        if (!recentAddresses.ContainsKey(d.address) && recentAddresses.Count >= 24) return;
        var sb = new StringBuilder();
        for (int i = 0; i < d.floats.Count && i < 4; i++) sb.Append(d.floats[i].ToString("F2")).Append(' ');
        if (d.strings.Count > 0) sb.Insert(0, d.strings[0] + " ");
        recentAddresses[d.address] = sb.ToString();
        recentAddressTimes[d.address] = now;
    }

    void OnGUI()
    {
        if (!showMonitorHUD) return;

        float w = 380f, h = 330f;
        float x = 10f, y = Screen.height - h - 10f;
        GUI.Box(new Rect(x, y, w, h), "SlimeVR 受信モニター");
        GUILayout.BeginArea(new Rect(x + 10f, y + 22f, w - 20f, h - 28f));

        float now = Time.time;
        bool isActivelyReceiving = totalPacketsReceived > 0 && (now - lastPacketTime) < 2.0f;
        bool hasReceivedBefore = totalPacketsReceived > 0;

        var status = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        if (bindError != null)
        {
            status.normal.textColor = Color.red;
            GUILayout.Label($"ポート {port}: バインド失敗 ({bindError})", status);
        }
        else if (isActivelyReceiving)
        {
            status.normal.textColor = Color.green;
            GUILayout.Label($"ポート {port}: 受信中 ● (通信正常)", status);
        }
        else if (hasReceivedBefore)
        {
            status.normal.textColor = new Color(1f, 0.6f, 0.1f); // オレンジ
            GUILayout.Label($"ポート {port}: 一時途絶 △ (最終受信: {(now - lastPacketTime):F1}秒前)", status);
        }
        else
        {
            status.normal.textColor = Color.yellow;
            GUILayout.Label($"ポート {port}: 待機中 ○ (データ未着)", status);
        }

        GUILayout.Label($"パケット {totalPacketsReceived} / メッセージ {totalMessagesProcessed} / キャリブ {(trackingCalibrated ? "済" : "未")} [C]");

        GUILayout.BeginHorizontal();
        for (int i = 0; i < 4; i++) Lamp(i);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        for (int i = 4; i < 7; i++) Lamp(i);
        GUILayout.EndHorizontal();

        if (idTrackers.Count > 0)
            GUILayout.Label($"番号割り当て: {DescribeMapping()}");

        var small = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        small.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        GUILayout.Label("受信アドレス (直近):", small);
        int shown = 0;
        foreach (var kv in recentAddresses)
        {
            if (shown >= 10) break;
            if (now - recentAddressTimes[kv.Key] > 5f) continue;
            GUILayout.Label($"{kv.Key}  {kv.Value}", small);
            shown++;
        }
        if (shown == 0) GUILayout.Label("(なし)", small);

        GUILayout.EndArea();
    }

    private void Lamp(int i)
    {
        bool active = roles[i] != null && Recent(roles[i]);
        var st = new GUIStyle(GUI.skin.label);
        st.normal.textColor = active ? Color.green : Color.gray;
        GUILayout.Label($"{(active ? "●" : "○")}{RoleLabels[i]}", st, GUILayout.Width(80));
    }
}
