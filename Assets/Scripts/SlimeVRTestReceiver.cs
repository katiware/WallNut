using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

/// <summary>
/// SlimeVRおよびVRChat OSC形式のパケットを受信し、3Dキャラクターのボーンへ同期するレシーバー。
/// - OSC Bundle ("#bundle") の完全対応（実機SlimeVRの一括パケットに対応）
/// - 7点トラッキング完全対応（胸1点、両手握り2点、両膝2点、両足首2点 ※頭なし対応）
/// - 位置(Position)と回転(Rotation)の双方をボーンへ正確に同期
/// - 右手系(OpenVR/SlimeVR)から左手系(Unity)への座標軸変換
/// - 画面上でのリアルタイム受信状態可視化HUD（ポート、受信数、各部位の受信ランプ）
/// </summary>
public class SlimeVRTestReceiver : MonoBehaviour
{
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = false;

    [Header("通信設定")]
    [Tooltip("SlimeVRがOSCを送信するポート。デフォルトは9000です")]
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

    [Header("トラッキング補正")]
    [Tooltip("しゃがみ時に胸の位置(Y座標)を反映するスケーリング係数")]
    public float positionMultiplier = 1.0f;
    [Tooltip("手の位置情報から腕の向きを直接計算する (握りこみトラッカーに最適)")]
    public bool usePositionForArms = true;

    [Header("デバッグ表示")]
    [Tooltip("受信したOSCデータをコンソールに表示する")]
    public bool showDebugLog = false;
    [Tooltip("画面上に受信状況モニターHUDを表示する")]
    public bool showMonitorHUD = true;

    // 受信パケットの統計
    public int totalPacketsReceived = 0;
    public int totalMessagesProcessed = 0;
    public float lastPacketTime = -1f;
    public string lastReceivedAddress = "---";

    // 7部位の最終受信時刻（ランプ表示用）
    private float lastChestTime = -1f;
    private float lastLeftArmTime = -1f;
    private float lastRightArmTime = -1f;
    private float lastLeftKneeTime = -1f;
    private float lastRightKneeTime = -1f;
    private float lastLeftFootTime = -1f;
    private float lastRightFootTime = -1f;
    private float lastHeadTime = -1f;

    // ボーンの初期位置（相対移動用）
    private Vector3 chestInitialLocalPos = Vector3.zero;
    private bool initialPositionsRecorded = false;

    // メインスレッド（Update）で処理するためのキュー
    private Queue<ParsedOscData> messageQueue = new Queue<ParsedOscData>();
    private object queueLock = new object();

    public struct ParsedOscData
    {
        public string address;
        public List<float> floats;
        public List<string> strings;
    }

    void Start()
    {
        AutoFindBonesIfEmpty();
        RecordInitialPositions();
        StartReceiving();
    }

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
        }
    }

    private void RecordInitialPositions()
    {
        if (chestBone != null)
        {
            chestInitialLocalPos = chestBone.localPosition;
            initialPositionsRecorded = true;
        }
    }

    void Update()
    {
        if (!initialPositionsRecorded && chestBone != null)
        {
            RecordInitialPositions();
        }

        // メインスレッドでキューからデータを取り出してボーンを更新
        lock (queueLock)
        {
            while (messageQueue.Count > 0)
            {
                ParsedOscData data = messageQueue.Dequeue();
                ProcessParsedData(data);
            }
        }
    }

    private void ProcessParsedData(ParsedOscData data)
    {
        totalMessagesProcessed++;
        string addr = data.address.ToLower();
        lastReceivedAddress = data.address;
        float now = Time.time;

        // VMC (Virtual Motion Capture) 形式の処理: /VMC/Ext/Bone/Pos (boneName, px, py, pz, rx, ry, rz, rw)
        if (addr.StartsWith("/vmc/ext/bone/pos") && data.strings.Count > 0 && data.floats.Count >= 7)
        {
            string boneName = data.strings[0].ToLower();
            Vector3 pos = new Vector3(-data.floats[0], data.floats[1], data.floats[2]); // X軸反転（右手系→左手系）
            Quaternion rot = new Quaternion(-data.floats[3], data.floats[4], -data.floats[5], data.floats[6]);
            ApplyToBoneByName(boneName, pos, rot, now);
            return;
        }

        // 標準OSC / VRChat形式の解析
        if (data.floats.Count == 0) return;

        bool hasPos = false;
        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;

        if (data.floats.Count >= 7)
        {
            // VRChat OSC標準: [pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, rot.w]
            hasPos = true;
            position = new Vector3(-data.floats[0], data.floats[1], data.floats[2]); // X軸反転
            rotation = new Quaternion(-data.floats[3], data.floats[4], -data.floats[5], data.floats[6]);
        }
        else if (data.floats.Count == 6)
        {
            // 位置 + オイラー角: [pos.x, pos.y, pos.z, pitch, yaw, roll]
            hasPos = true;
            position = new Vector3(-data.floats[0], data.floats[1], data.floats[2]);
            rotation = Quaternion.Euler(data.floats[3], -data.floats[4], -data.floats[5]);
        }
        else if (data.floats.Count == 4)
        {
            // クォータニオンのみ: [rot.x, rot.y, rot.z, rot.w]
            rotation = new Quaternion(-data.floats[0], data.floats[1], -data.floats[2], data.floats[3]);
        }
        else if (data.floats.Count == 3)
        {
            // オイラー角または位置
            if (addr.Contains("position") || addr.Contains("pos"))
            {
                hasPos = true;
                position = new Vector3(-data.floats[0], data.floats[1], data.floats[2]);
            }
            else
            {
                rotation = Quaternion.Euler(data.floats[0], data.floats[1], data.floats[2]);
            }
        }

        if (showDebugLog)
        {
            Debug.Log($"[OSC受信] {data.address} ({data.floats.Count}値) Rot:{rotation.eulerAngles:F1} Pos:{position:F2}");
        }

        // --- 1. 胸 (Chest / Spine / Waist: 1点) ---
        if (addr.Contains("chest") || addr.Contains("waist") || addr.Contains("spine") || addr.Contains("tracker/1") || addr.Contains("tracker/waist"))
        {
            lastChestTime = now;
            if (chestBone != null)
            {
                chestBone.localRotation = rotation;
                if (hasPos)
                {
                    // 胸の上下動（しゃがみ）を初期位置からの変位として反映
                    float yOffset = (position.y - 1.0f) * positionMultiplier;
                    chestBone.localPosition = new Vector3(chestInitialLocalPos.x, chestInitialLocalPos.y + yOffset, chestInitialLocalPos.z);
                }
            }
        }
        // --- 2. 左手 / 左腕 (Left Hand / Controller / Arm: 握りこみ) ---
        else if (addr.Contains("left") && (addr.Contains("hand") || addr.Contains("controller") || addr.Contains("arm")))
        {
            lastLeftArmTime = now;
            if (leftArmBone != null)
            {
                if (hasPos && usePositionForArms && chestBone != null)
                {
                    // 手の位置から腕の向きを計算して直感的に動かす
                    Vector3 shoulderPos = leftArmBone.position;
                    Vector3 targetWorldPos = transform.TransformPoint(position);
                    Vector3 dir = (targetWorldPos - shoulderPos).normalized;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        // 左腕は初期状態が-X方向なので、その方向へ合わせる
                        leftArmBone.rotation = Quaternion.FromToRotation(Vector3.left, dir);
                    }
                }
                else
                {
                    leftArmBone.localRotation = rotation;
                }
            }
        }
        // --- 3. 右手 / 右腕 (Right Hand / Controller / Arm: 握りこみ) ---
        else if (addr.Contains("right") && (addr.Contains("hand") || addr.Contains("controller") || addr.Contains("arm")))
        {
            lastRightArmTime = now;
            if (rightArmBone != null)
            {
                if (hasPos && usePositionForArms && chestBone != null)
                {
                    Vector3 shoulderPos = rightArmBone.position;
                    Vector3 targetWorldPos = transform.TransformPoint(position);
                    Vector3 dir = (targetWorldPos - shoulderPos).normalized;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        // 右腕は初期状態が+X方向
                        rightArmBone.rotation = Quaternion.FromToRotation(Vector3.right, dir);
                    }
                }
                else
                {
                    rightArmBone.localRotation = rotation;
                }
            }
        }
        // --- 4. 左膝 (Left Knee) ---
        else if (addr.Contains("left") && addr.Contains("knee"))
        {
            lastLeftKneeTime = now;
            if (leftKneeBone != null) leftKneeBone.localRotation = rotation;
            else if (leftFootBone != null) leftFootBone.localRotation = rotation;
        }
        // --- 5. 右膝 (Right Knee) ---
        else if (addr.Contains("right") && addr.Contains("knee"))
        {
            lastRightKneeTime = now;
            if (rightKneeBone != null) rightKneeBone.localRotation = rotation;
            else if (rightFootBone != null) rightFootBone.localRotation = rotation;
        }
        // --- 6. 左足 / 左足首 (Left Foot / Ankle) ---
        else if (addr.Contains("left") && (addr.Contains("foot") || addr.Contains("ankle")))
        {
            lastLeftFootTime = now;
            if (leftFootBone != null) leftFootBone.localRotation = rotation;
        }
        // --- 7. 右足 / 右足首 (Right Foot / Ankle) ---
        else if (addr.Contains("right") && (addr.Contains("foot") || addr.Contains("ankle")))
        {
            lastRightFootTime = now;
            if (rightFootBone != null) rightFootBone.localRotation = rotation;
        }
        // --- 8. 頭 (Head: 任意・未装着でも無害) ---
        else if (addr.Contains("head"))
        {
            lastHeadTime = now;
            if (headBone != null) headBone.localRotation = rotation;
        }
    }

    private void ApplyToBoneByName(string boneName, Vector3 pos, Quaternion rot, float now)
    {
        if (boneName.Contains("chest") || boneName.Contains("spine") || boneName.Contains("hips"))
        {
            lastChestTime = now;
            if (chestBone != null) { chestBone.localRotation = rot; }
        }
        else if (boneName.Contains("lefthand") || boneName.Contains("leftwrist") || boneName.Contains("leftlowerarm"))
        {
            lastLeftArmTime = now;
            if (leftArmBone != null) { leftArmBone.localRotation = rot; }
        }
        else if (boneName.Contains("righthand") || boneName.Contains("rightwrist") || boneName.Contains("rightlowerarm"))
        {
            lastRightArmTime = now;
            if (rightArmBone != null) { rightArmBone.localRotation = rot; }
        }
        else if (boneName.Contains("leftlowerleg") || boneName.Contains("leftknee"))
        {
            lastLeftKneeTime = now;
            if (leftKneeBone != null) { leftKneeBone.localRotation = rot; }
        }
        else if (boneName.Contains("rightlowerleg") || boneName.Contains("rightknee"))
        {
            lastRightKneeTime = now;
            if (rightKneeBone != null) { rightKneeBone.localRotation = rot; }
        }
        else if (boneName.Contains("leftfoot"))
        {
            lastLeftFootTime = now;
            if (leftFootBone != null) { leftFootBone.localRotation = rot; }
        }
        else if (boneName.Contains("rightfoot"))
        {
            lastRightFootTime = now;
            if (rightFootBone != null) { rightFootBone.localRotation = rot; }
        }
        else if (boneName.Contains("head"))
        {
            lastHeadTime = now;
            if (headBone != null) { headBone.localRotation = rot; }
        }
    }

    private void StartReceiving()
    {
        try
        {
            udpClient = new UdpClient(port);
            isRunning = true;
            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            Debug.Log($"📡 [SlimeVR Receiver] ポート {port} でOSCデータの受信待機を開始しました。(#bundle対応済)");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ [SlimeVR Receiver] ポート {port} のバインドに失敗しました。\n{e.Message}");
        }
    }

    private void ReceiveData()
    {
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
        while (isRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref anyIP);
                totalPacketsReceived++;
                lastPacketTime = Time.time;
                ParseIncomingPacket(data, 0, data.Length);
            }
            catch (SocketException) { /* 終了時の例外は無視 */ }
            catch (System.Exception e) { Debug.LogWarning(e.ToString()); }
        }
    }

    /// <summary>
    /// パケットが OSC Bundle ("#bundle") か単体 OSC Message かを判定して処理
    /// </summary>
    private void ParseIncomingPacket(byte[] data, int offset, int length)
    {
        if (data == null || length < 4) return;

        // 1. OSC Bundle ("#bundle") か判定
        if (data[offset] == '#' && length >= 16)
        {
            string header = Encoding.ASCII.GetString(data, offset, 7);
            if (header == "#bundle")
            {
                ParseOscBundle(data, offset, length);
                return;
            }
        }

        // 2. 単体 OSC Message として処理
        ParseOscMessage(data, offset, length);
    }

    /// <summary>
    /// OSC Bundle ("#bundle") の再帰パース
    /// 構造: "#bundle\0" (8 bytes) + TimeTag (8 bytes) + 各要素 [Size(4 bytes) + Content] の繰り返し
    /// </summary>
    private void ParseOscBundle(byte[] data, int offset, int length)
    {
        int index = offset + 16; // "#bundle\0" (8B) + TimeTag (8B) をスキップ
        int end = offset + length;

        while (index + 4 <= end)
        {
            // 各要素のサイズ (int32, Big-Endian)
            int elementSize = ReadInt32BigEndian(data, ref index);
            if (elementSize <= 0 || index + elementSize > end) break;

            // 要素を再帰的にパース（Bundleの中にBundleが含まれるケースも対応）
            ParseIncomingPacket(data, index, elementSize);
            index += elementSize;
        }
    }

    /// <summary>
    /// 単体 OSC Message のパース
    /// 構造: Address (null終端+4Bパディング) + TypeTag (null終端+4Bパディング) + 引数群
    /// </summary>
    private void ParseOscMessage(byte[] data, int offset, int length)
    {
        try
        {
            int index = offset;
            int end = offset + length;

            // 1. アドレスの読み取り
            string address = ReadOscString(data, ref index, end);
            if (string.IsNullOrEmpty(address) || !address.StartsWith("/")) return;

            // 2. 型タグの読み取り (例: ",ffff" or ",sfffffff")
            string typeTag = ReadOscString(data, ref index, end);
            if (string.IsNullOrEmpty(typeTag) || !typeTag.StartsWith(",")) return;

            // 3. 引数の読み取り
            List<float> floats = new List<float>();
            List<string> strings = new List<string>();

            for (int i = 1; i < typeTag.Length; i++)
            {
                char tag = typeTag[i];
                if (tag == 'f')
                {
                    floats.Add(ReadFloatBigEndian(data, ref index, end));
                }
                else if (tag == 'i')
                {
                    floats.Add((float)ReadInt32BigEndian(data, ref index));
                }
                else if (tag == 's')
                {
                    strings.Add(ReadOscString(data, ref index, end));
                }
                else if (tag == 'd') // double
                {
                    if (index + 8 <= end)
                    {
                        byte[] dBytes = new byte[8];
                        for (int b = 0; b < 8; b++) dBytes[b] = data[index + (7 - b)];
                        floats.Add((float)System.BitConverter.ToDouble(dBytes, 0));
                        index += 8;
                    }
                }
                else if (tag == 'T') // True
                {
                    floats.Add(1f);
                }
                else if (tag == 'F') // False
                {
                    floats.Add(0f);
                }
                else
                {
                    // 未知の型は4バイト進めてスキップ
                    index += 4;
                }
            }

            lock (queueLock)
            {
                messageQueue.Enqueue(new ParsedOscData
                {
                    address = address,
                    floats = floats,
                    strings = strings
                });
            }
        }
        catch (System.Exception e)
        {
            if (showDebugLog) Debug.LogWarning($"[OSC Message Parse Error] {e.Message}");
        }
    }

    private string ReadOscString(byte[] data, ref int index, int end)
    {
        if (index >= end) return string.Empty;
        int start = index;
        while (index < end && data[index] != 0) index++;

        string str = Encoding.ASCII.GetString(data, start, index - start);
        index++; // null文字

        // 4バイト境界アライメント
        int padding = (4 - ((index - start) % 4)) % 4;
        index += padding;
        return str;
    }

    private float ReadFloatBigEndian(byte[] data, ref int index, int end)
    {
        if (index + 4 > end) return 0f;

        byte[] bytes = new byte[4];
        bytes[0] = data[index + 3];
        bytes[1] = data[index + 2];
        bytes[2] = data[index + 1];
        bytes[3] = data[index + 0];

        float value = System.BitConverter.ToSingle(bytes, 0);
        index += 4;
        return value;
    }

    private int ReadInt32BigEndian(byte[] data, ref int index)
    {
        if (index + 4 > data.Length) return 0;

        int value = (data[index] << 24) | (data[index + 1] << 16) | (data[index + 2] << 8) | data[index + 3];
        index += 4;
        return value;
    }

    void OnGUI()
    {
        if (!showMonitorHUD) return;

        // 画面左下にSlimeVR受信モニターを半透明ボックスで常時表示
        float w = 260;
        float h = 185;
        float x = 10;
        float y = Screen.height - h - 10;

        GUI.Box(new Rect(x, y, w, h), "📡 SlimeVR 受信モニター");

        float now = Time.time;
        bool isReceiving = (now - lastPacketTime) < 1.0f && totalPacketsReceived > 0;

        GUILayout.BeginArea(new Rect(x + 10, y + 25, w - 20, h - 30));

        // 通信ステータス
        GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
        statusStyle.fontStyle = FontStyle.Bold;
        statusStyle.normal.textColor = isReceiving ? Color.green : Color.red;
        GUILayout.Label($"ポート {port}: {(isReceiving ? "受信中 ● (OK)" : "待機中 / 停止中 ○")}", statusStyle);

        GUILayout.Label($"パケット数: {totalPacketsReceived} 件 ({totalMessagesProcessed} メッセージ)");

        // 7部位の受信ランプ表示
        GUILayout.Space(3);
        GUILayout.Label("【7点トラッカー受信状態】");

        GUILayout.BeginHorizontal();
        DrawTrackerLamp("胸", (now - lastChestTime) < 1.5f);
        DrawTrackerLamp("左手", (now - lastLeftArmTime) < 1.5f);
        DrawTrackerLamp("右手", (now - lastRightArmTime) < 1.5f);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawTrackerLamp("左膝", (now - lastLeftKneeTime) < 1.5f);
        DrawTrackerLamp("右膝", (now - lastRightKneeTime) < 1.5f);
        DrawTrackerLamp("左足", (now - lastLeftFootTime) < 1.5f);
        DrawTrackerLamp("右足", (now - lastRightFootTime) < 1.5f);
        GUILayout.EndHorizontal();

        GUILayout.Space(3);
        GUIStyle addrStyle = new GUIStyle(GUI.skin.label);
        addrStyle.fontSize = 10;
        addrStyle.normal.textColor = Color.gray;
        string shortAddr = lastReceivedAddress.Length > 32 ? "..." + lastReceivedAddress.Substring(lastReceivedAddress.Length - 29) : lastReceivedAddress;
        GUILayout.Label($"最新: {shortAddr}", addrStyle);

        GUILayout.EndArea();
    }

    private void DrawTrackerLamp(string label, bool active)
    {
        GUIStyle lampStyle = new GUIStyle(GUI.skin.label);
        lampStyle.normal.textColor = active ? Color.green : Color.gray;
        GUILayout.Label($"{(active ? "🟢" : "⚪")}{label} ", lampStyle);
    }

    void OnDestroy()
    {
        isRunning = false;
        if (udpClient != null) udpClient.Close();
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(500);
    }
}

