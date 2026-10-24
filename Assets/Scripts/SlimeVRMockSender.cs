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

    [Header("モック送信の有効化")]
    [Tooltip("擬似データを送信するかどうか。実機SlimeVRをテストする際はOFF(false)にしてください")]
    public bool enableMockSending = false;

    [Header("送信ポーズモード（キーボードで切り替え可能）")]
    [Tooltip("0: 自動うねうね, 1: 棒立ち(N), 2: バンザイ(B), 3: しゃがみ(S), 4: Tポーズ(T), 5: 右腕上げ(R), 6: 左腕上げ(L), 7: フラミンゴ(F)")]
    public int simulatedPoseMode = 1;

    private UdpClient udpClient;
    private float timer;

    void Start()
    {
        udpClient = new UdpClient();
        simulatedPoseMode = 1; // 棒立ち(Stand)
        Debug.Log($"[Mock Sender] 擬似OSC送信機 準備完了 (初期状態: {(enableMockSending ? "有効" : "無効(実機優先)")})");
    }

    void Update()
    {
        // キー入力でモック送信のON/OFFを切り替え可能 [M]キー
        if (Input.GetKeyDown(KeyCode.M))
        {
            enableMockSending = !enableMockSending;
            Debug.Log($"📡 [Mock Sender] 擬似OSC送信: {(enableMockSending ? "有効化 (テスト送信中)" : "停止 (実機SlimeVR優先)")}");
        }

        if (!enableMockSending) return;

        // キー入力で送信ポーズモードを切り替え
        if (Input.GetKeyDown(KeyCode.N)) SetPoseMode(1); // 棒立ち
        else if (Input.GetKeyDown(KeyCode.B)) SetPoseMode(2); // バンザイ
        else if (Input.GetKeyDown(KeyCode.S)) SetPoseMode(3); // しゃがみ
        else if (Input.GetKeyDown(KeyCode.T)) SetPoseMode(4); // Tポーズ
        else if (Input.GetKeyDown(KeyCode.R)) SetPoseMode(5); // 右腕上げ
        else if (Input.GetKeyDown(KeyCode.L)) SetPoseMode(6); // 左腕上げ
        else if (Input.GetKeyDown(KeyCode.F)) SetPoseMode(7); // 片足立ち (Flamingo)
        else if (Input.GetKeyDown(KeyCode.Space)) SetPoseMode(0); // 自動うねうね
        else if (Input.GetKeyDown(KeyCode.C))
        {
            // キャリブレーション実行
            PlayerDummy dummy = FindObjectOfType<PlayerDummy>();
            if (dummy != null) dummy.Calibrate();
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
        string name = mode switch
        {
            1 => "【1. 棒立ち (Stand)】",
            2 => "【2. バンザイ (Banzai)】",
            3 => "【3. しゃがみ (Squat)】",
            4 => "【4. Tポーズ (T-Pose)】",
            5 => "【5. 右腕上げ (RightArm)】",
            6 => "【6. 左腕上げ (LeftArm)】",
            7 => "【7. 片足立ち (Flamingo)】",
            _ => "【0. 自動デモうねうね】"
        };
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
                headPitch = -15f;
                headPan = 0f;
                leftArmZ = -70f;  // 左腕を上げる(-Z)
                rightArmZ = 70f;  // 右腕を上げる(+Z)
                leftFootX = 0f; rightFootX = 0f;
                break;

            case 3: // しゃがみ (Squat) - 脚・膝を曲げる
                headPitch = 20f;
                headPan = 0f;
                leftArmZ = 45f; rightArmZ = -45f; // 腕は下げ気味
                leftFootX = 45f; // 両足を曲げる
                rightFootX = 45f;
                break;

            case 4: // Tポーズ (T-Pose) - 両腕を水平に広げる
                headPitch = 0f; headPan = 0f;
                leftArmZ = 0f;   // 水平
                rightArmZ = 0f;  // 水平
                leftFootX = 0f; rightFootX = 0f;
                break;

            case 5: // 右腕上げ (RightArmUp) - 右腕だけ上げて左腕は下げる
                headPitch = 0f; headPan = 0f;
                leftArmZ = 70f;  // 左腕下げる
                rightArmZ = 70f; // 右腕上げる
                leftFootX = 0f; rightFootX = 0f;
                break;

            case 6: // 左腕上げ (LeftArmUp) - 左腕だけ上げて右腕は下げる
                headPitch = 0f; headPan = 0f;
                leftArmZ = -70f;  // 左腕上げる
                rightArmZ = -70f; // 右腕下げる
                leftFootX = 0f; rightFootX = 0f;
                break;

            case 7: // 片足立ち (Flamingo) - 右足だけ曲げる
                headPitch = 0f; headPan = 0f;
                leftArmZ = 70f; rightArmZ = -70f; // 腕は下ろす
                leftFootX = 0f;
                rightFootX = 55f; // 右足だけ大きく曲げる
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
        // 画面左上にテスト用のポーズ切替UIを表示
        GUILayout.BeginArea(new Rect(10, 10, 240, 340), "【擬似OSC送信操作】", GUI.skin.window);
        
        // モック送信のON/OFF切り替えボタン
        GUI.color = enableMockSending ? Color.cyan : Color.white;
        string toggleText = enableMockSending ? "🟢 擬似送信: 稼働中 [M]" : "⚪ 擬似送信: 停止 (実機優先) [M]";
        if (GUILayout.Button(toggleText, GUILayout.Height(26)))
        {
            enableMockSending = !enableMockSending;
        }
        GUI.color = Color.white;
        GUILayout.Space(4);

        if (!enableMockSending)
        {
            GUILayout.Label("※実機SlimeVRが優先されます。\nモックテストを行う場合は上のボタンまたは[M]キーで有効化してください。");
            GUILayout.Space(5);
            if (GUILayout.Button("🎯 キャリブレーション [C]"))
            {
                PlayerDummy dummy = FindObjectOfType<PlayerDummy>();
                if (dummy != null) dummy.Calibrate();
            }
            GUILayout.EndArea();
            return;
        }

        string currentText = simulatedPoseMode switch
        {
            1 => "1. 棒立ち [N]",
            2 => "2. バンザイ [B]",
            3 => "3. しゃがみ [S]",
            4 => "4. Tポーズ [T]",
            5 => "5. 右腕上げ [R]",
            6 => "6. 左腕上げ [L]",
            7 => "7. 片足立ち [F]",
            _ => "0. 自動うねうね [Space]"
        };
        GUILayout.Label($"送信中: {currentText}");
        GUILayout.Space(5);

        if (GUILayout.Button("1. 棒立ち [N]")) SetPoseMode(1);
        if (GUILayout.Button("2. バンザイ [B]")) SetPoseMode(2);
        if (GUILayout.Button("3. しゃがみ [S]")) SetPoseMode(3);
        if (GUILayout.Button("4. Tポーズ [T]")) SetPoseMode(4);
        if (GUILayout.Button("5. 右腕上げ [R]")) SetPoseMode(5);
        if (GUILayout.Button("6. 左腕上げ [L]")) SetPoseMode(6);
        if (GUILayout.Button("7. 片足立ち [F]")) SetPoseMode(7);
        if (GUILayout.Button("0. 自動デモ [Space]")) SetPoseMode(0);
        
        GUILayout.Space(5);
        if (GUILayout.Button("🎯 キャリブレーション [C]"))
        {
            PlayerDummy dummy = FindObjectOfType<PlayerDummy>();
            if (dummy != null) dummy.Calibrate();
        }

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
