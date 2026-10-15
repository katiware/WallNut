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

    [Header("キャラクターのボーン（関節）割り当て")]
    [Tooltip("頭のTransform")]
    public Transform headBone;
    [Tooltip("左足のTransform")]
    public Transform leftFootBone;
    [Tooltip("右足のTransform")]
    public Transform rightFootBone;
    [Tooltip("左腕のTransform")]
    public Transform leftArmBone;
    [Tooltip("右腕のTransform")]
    public Transform rightArmBone;

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
        StartReceiving();
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
        if (msg.values == null || msg.values.Count < 3) return;

        if (showDebugLog)
        {
            Debug.Log($"[OSC受信] {msg.address} -> ({msg.values[0]:F1}, {msg.values[1]:F1}, {msg.values[2]:F1})");
        }

        // OSCで送られてくる3つの数値をオイラー角(x, y, z)として解釈し、Quaternionに変換
        Vector3 receivedEuler = new Vector3(msg.values[0], msg.values[1], msg.values[2]);
        Quaternion targetRotation = Quaternion.Euler(receivedEuler);

        // アドレスに応じて各ボーンに回転を適用
        switch (msg.address)
        {
            case "/tracking/vrsystem/head/pose":
                if (headBone != null)
                {
                    headBone.localRotation = targetRotation;
                }
                else if (showDebugLog)
                {
                    Debug.LogWarning("[SlimeVR] headBone が設定されていません！インスペクターで割り当ててください。");
                }
                break;
            case "/tracking/vrsystem/leftfoot/pose":
                if (leftFootBone != null) leftFootBone.localRotation = targetRotation;
                break;
            case "/tracking/vrsystem/rightfoot/pose":
                if (rightFootBone != null) rightFootBone.localRotation = targetRotation;
                break;
            case "/tracking/vrsystem/leftarm/pose":
                if (leftArmBone != null) leftArmBone.localRotation = targetRotation;
                break;
            case "/tracking/vrsystem/rightarm/pose":
                if (rightArmBone != null) rightArmBone.localRotation = targetRotation;
                break;
            default:
                break;
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
