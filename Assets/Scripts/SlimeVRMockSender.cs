using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System;

public class SlimeVRMockSender : MonoBehaviour
{
    [Tooltip("送信先のポート番号（SlimeVRTestReceiverと同じポート）")]
    public int port = 9000;
    [Tooltip("送信先のIPアドレス（自分自身の場合は 127.0.0.1）")]
    public string targetIP = "127.0.0.1";
    [Tooltip("送信間隔（秒）")]
    public float sendInterval = 0.05f;

    [Header("送信ポーズモード（キーボードで切り替え可能）")]
    [Tooltip("0: 自動うねうね, 1: 棒立ち(N), 2: バンザイ(B), 3: しゃがみ(S)")]
    public int simulatedPoseMode = 1;

    private UdpClient udpClient;
    private float timer;

    void Start()
    {
        udpClient = new UdpClient();
        simulatedPoseMode = 1; // 起動時は必ず棒立ち(Stand)からスタート
        Debug.Log($"[Mock Sender] 擬似OSCデータの送信準備完了。送信先: {targetIP}:{port}");
        Debug.Log("💡 画面のボタン、またはキーボードで送信ポーズを変更できます: [B] バンザイ, [S] しゃがみ, [N] 棒立ち, [Space] 自動うねうね");
    }

    void Update()
    {
        // キー入力で送信ポーズモードを切り替え
        if (Input.GetKeyDown(KeyCode.B))
        {
            SetPoseMode(2); // バンザイ
        }
        else if (Input.GetKeyDown(KeyCode.S))
        {
            SetPoseMode(3); // しゃがみ
        }
        else if (Input.GetKeyDown(KeyCode.N))
        {
            SetPoseMode(1); // 棒立ち
        }
        else if (Input.GetKeyDown(KeyCode.Space))
        {
            SetPoseMode(0); // 自動うねうね
        }

        timer += Time.deltaTime;
        if (timer >= sendInterval)
        {
            timer = 0f;
            SendCurrentPose();
        }
    }

    public void SetPoseMode(int mode)
    {
        simulatedPoseMode = mode;
        string name = mode == 1 ? "【棒立ち】" : (mode == 2 ? "【バンザイ】" : (mode == 3 ? "【しゃがみ】" : "【自動うねうね】"));
        Debug.Log($"📡 [Mock Sender] 送信ポーズ切替: {name}");
    }

    void SendCurrentPose()
    {
        float time = Time.time;
        float headPitch = 0f, headPan = 0f;
        float leftArmZ = 0f, rightArmZ = 0f;
        float leftFootX = 0f, rightFootX = 0f;

        switch (simulatedPoseMode)
        {
            case 0: // 自動うねうねモード
                headPitch = Mathf.Sin(time * 3f) * 35f;
                headPan = Mathf.Cos(time * 2f) * 45f;
                leftFootX = Mathf.Cos(time * 3f) * 30f;
                rightFootX = -leftFootX;
                leftArmZ = Mathf.Sin(time * 2f) * 60f;
                rightArmZ = -leftArmZ;
                break;

            case 1: // 棒立ち (Stand) - 腕を自然に下ろす
                headPitch = 0f; headPan = 0f;
                leftArmZ = 70f;   // 左腕を下げる(+Z)
                rightArmZ = -70f; // 右腕を下げる(-Z)
                leftFootX = 0f; rightFootX = 0f;
                break;

            case 2: // バンザイ (Banzai) - 両腕を高く上げる
                headPitch = -15f; // 少し上を向く
                headPan = 0f;
                leftArmZ = -70f;  // 左腕を上げる(-Z)
                rightArmZ = 70f;  // 右腕を上げる(+Z)
                leftFootX = 0f; rightFootX = 0f;
                break;

            case 3: // しゃがみ (Squat) - 脚・膝を曲げる
                headPitch = 20f;
                headPan = 0f;
                leftArmZ = 45f; rightArmZ = -45f; // 腕は下げ気味
                leftFootX = 45f; // 足首・脚を曲げる
                rightFootX = 45f;
                break;
        }

        SendDummyData("/tracking/vrsystem/head/pose", headPitch, headPan, 0f);
        SendDummyData("/tracking/vrsystem/leftfoot/pose", leftFootX, 0f, 0f);
        SendDummyData("/tracking/vrsystem/rightfoot/pose", rightFootX, 0f, 0f);
        SendDummyData("/tracking/vrsystem/leftarm/pose", 0f, 0f, leftArmZ);
        SendDummyData("/tracking/vrsystem/rightarm/pose", 0f, 0f, rightArmZ);
    }

    void OnGUI()
    {
        // 画面左上にテスト用のポーズ切替UIを表示（マウスクリックで即座に切替可能）
        GUILayout.BeginArea(new Rect(10, 10, 220, 200), "【送信ポーズ操作】", GUI.skin.window);
        
        string currentText = simulatedPoseMode == 1 ? "棒立ち(白)" : (simulatedPoseMode == 2 ? "バンザイ(赤)" : (simulatedPoseMode == 3 ? "しゃがみ(青)" : "自動うねうね"));
        GUILayout.Label($"送信中: {currentText}");

        if (GUILayout.Button("1. 棒立ち [N]")) SetPoseMode(1);
        if (GUILayout.Button("2. バンザイ [B]")) SetPoseMode(2);
        if (GUILayout.Button("3. しゃがみ [S]")) SetPoseMode(3);
        if (GUILayout.Button("0. 自動デモ [Space]")) SetPoseMode(0);

        GUILayout.EndArea();
    }

    void SendDummyData(string address, float x, float y, float z)
    {
        try
        {
            List<byte> packet = new List<byte>();
            
            // 1. Address
            packet.AddRange(Encoding.ASCII.GetBytes(address));
            packet.Add(0);
            int padding = 4 - (packet.Count % 4);
            if (padding < 4) {
                for (int i=0; i<padding; i++) packet.Add(0);
            }

            // 2. Type Tag
            packet.AddRange(Encoding.ASCII.GetBytes(",fff"));
            packet.Add(0);
            padding = 4 - (packet.Count % 4);
            if (padding < 4) {
                for (int i=0; i<padding; i++) packet.Add(0);
            }

            // 3. Arguments (Big-endian floats)
            packet.AddRange(GetBigEndianBytes(x));
            packet.AddRange(GetBigEndianBytes(y));
            packet.AddRange(GetBigEndianBytes(z));
            
            byte[] data = packet.ToArray();
            udpClient.Send(data, data.Length, targetIP, port);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Mock Sender] 送信エラー: {e.Message}");
        }
    }

    private byte[] GetBigEndianBytes(float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        // C# defaults to little-endian on most architectures, OSC expects big-endian
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        return bytes;
    }

    void OnDestroy()
    {
        if (udpClient != null)
        {
            udpClient.Close();
        }
    }
}
