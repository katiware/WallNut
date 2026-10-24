using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public class SlimeVRTestReceiver : MonoBehaviour
{
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = false;

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
    [Tooltip("頭のTransform (任意)")]
    public Transform headBone;

    [Header("デバッグ")]
    [Tooltip("受信したOSCデータをコンソールに表示する")]
    public bool showDebugLog = true;

    // メインスレッド（Update）で処理するためのキュー
    private Queue<OscMessage> messageQueue = new Queue<OscMessage>();
    private object queueLock = new object();

    public struct OscMessage
    {
        public string address;
        public List<float> values;
    }

    void Start()
    {
        AutoFindBonesIfEmpty();
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

    void Update()
    {
        // メインスレッドで受信データを処理（ボーンの回転などUnityのAPIはここで呼ぶ）
        lock (queueLock)
        {
            while (messageQueue.Count > 0)
            {
                OscMessage msg = messageQueue.Dequeue();
                ProcessMessage(msg);
            }
        }
    }

    private void ProcessMessage(OscMessage msg)
    {
        if (msg.values == null || msg.values.Count == 0) return;

        // OSCデータ形式の判別（オイラー角 3つ、クォータニオン 4つ、または VRChat標準 Pos 3 + Quat 4 の7つ）
        Quaternion targetRotation = Quaternion.identity;

        if (msg.values.Count >= 7)
        {
            // VRChat OSC標準: [pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, rot.w]
            targetRotation = new Quaternion(msg.values[3], msg.values[4], msg.values[5], msg.values[6]);
        }
        else if (msg.values.Count >= 4)
        {
            // クォータニオン: [rot.x, rot.y, rot.z, rot.w]
            targetRotation = new Quaternion(msg.values[0], msg.values[1], msg.values[2], msg.values[3]);
        }
        else if (msg.values.Count >= 3)
        {
            // オイラー角: [x, y, z]
            targetRotation = Quaternion.Euler(msg.values[0], msg.values[1], msg.values[2]);
        }

        string addr = msg.address.ToLower();

        if (showDebugLog)
        {
            Debug.Log($"[OSC受信] {msg.address} ({msg.values.Count}値) -> Rot:{targetRotation.eulerAngles:F1}");
        }

        // --- 1. 胸 (Chest / Spine / Waist: 1点) ---
        if (addr.Contains("chest") || addr.Contains("waist") || addr.Contains("spine") || addr.Contains("tracker/1"))
        {
            if (chestBone != null) chestBone.localRotation = targetRotation;
        }
        // --- 2. 左手 / 左腕 (Left Hand / Controller / Arm: 握りこみ) ---
        else if (addr.Contains("left") && (addr.Contains("controller") || addr.Contains("hand") || addr.Contains("arm")))
        {
            if (leftArmBone != null) leftArmBone.localRotation = targetRotation;
        }
        // --- 3. 右手 / 右腕 (Right Hand / Controller / Arm: 握りこみ) ---
        else if (addr.Contains("right") && (addr.Contains("controller") || addr.Contains("hand") || addr.Contains("arm")))
        {
            if (rightArmBone != null) rightArmBone.localRotation = targetRotation;
        }
        // --- 4. 左膝 (Left Knee) ---
        else if (addr.Contains("left") && addr.Contains("knee"))
        {
            if (leftKneeBone != null) leftKneeBone.localRotation = targetRotation;
            else if (leftFootBone != null) leftFootBone.localRotation = targetRotation; // 膝ボーン未設定時は足で代用
        }
        // --- 5. 右膝 (Right Knee) ---
        else if (addr.Contains("right") && addr.Contains("knee"))
        {
            if (rightKneeBone != null) rightKneeBone.localRotation = targetRotation;
            else if (rightFootBone != null) rightFootBone.localRotation = targetRotation;
        }
        // --- 6. 左足 / 左足首 (Left Foot / Ankle) ---
        else if (addr.Contains("left") && (addr.Contains("foot") || addr.Contains("ankle")))
        {
            if (leftFootBone != null) leftFootBone.localRotation = targetRotation;
        }
        // --- 7. 右足 / 右足首 (Right Foot / Ankle) ---
        else if (addr.Contains("right") && (addr.Contains("foot") || addr.Contains("ankle")))
        {
            if (rightFootBone != null) rightFootBone.localRotation = targetRotation;
        }
        // --- 8. 頭 (Head: 送信されている場合のフォールバック) ---
        else if (addr.Contains("head"))
        {
            if (headBone != null) headBone.localRotation = targetRotation;
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
            Debug.Log($"[SlimeVR Test] ポート {port} でOSCデータの受信待機を開始しました。");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SlimeVR Test] ポートのバインドに失敗しました。\n{e.Message}");
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
                ParseOSC(data);
            }
            catch (SocketException) { /* 終了時の例外は無視 */ }
            catch (System.Exception e) { Debug.LogWarning(e.ToString()); }
        }
    }

    private void ParseOSC(byte[] data)
    {
        try
        {
            int index = 0;
            // 1. アドレスの読み取り (4バイト境界までのパディングをスキップ)
            string address = ReadString(data, ref index);
            if (string.IsNullOrEmpty(address)) return;

            // 2. 型タグの読み取り (例: ",fff")
            string typeTag = ReadString(data, ref index);
            if (string.IsNullOrEmpty(typeTag) || !typeTag.StartsWith(",")) return;

            // 3. 引数の読み取り
            List<float> floats = new List<float>();
            for (int i = 1; i < typeTag.Length; i++)
            {
                if (typeTag[i] == 'f') // float型の場合
                {
                    floats.Add(ReadFloat(data, ref index));
                }
                else
                {
                    // float以外（int, string等）が含まれる場合はスキップ
                    return;
                }
            }

            // キューに積む
            lock (queueLock)
            {
                messageQueue.Enqueue(new OscMessage { address = address, values = floats });
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[OSC Parse Error] {e.Message}");
        }
    }

    // OSCの文字列パース（null終端＋4バイト境界のパディング対応）
    private string ReadString(byte[] data, ref int index)
    {
        int start = index;
        while (index < data.Length && data[index] != 0) index++;
        
        string result = Encoding.ASCII.GetString(data, start, index - start);
        
        index++; // null文字分を進める
        int padding = 4 - ((index - start) % 4);
        if (padding < 4) index += padding; // 4の倍数になるようにインデックスを進める

        return result;
    }

    // OSCのfloat値パース（ビッグエンディアン対応）
    private float ReadFloat(byte[] data, ref int index)
    {
        if (index + 4 > data.Length) return 0f;

        // OSCはビッグエンディアン、C#は環境依存（通常リトルエンディアン）のため反転
        byte[] floatBytes = new byte[4];
        floatBytes[0] = data[index + 3];
        floatBytes[1] = data[index + 2];
        floatBytes[2] = data[index + 1];
        floatBytes[3] = data[index + 0];

        float value = System.BitConverter.ToSingle(floatBytes, 0);
        index += 4;
        return value;
    }

    void OnDestroy()
    {
        isRunning = false;
        if (udpClient != null) udpClient.Close();
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(500);
    }
}
