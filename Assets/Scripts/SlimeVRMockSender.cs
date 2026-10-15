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

    private UdpClient udpClient;
    private float timer;

    void Start()
    {
        udpClient = new UdpClient();
        Debug.Log($"[Mock Sender] 擬似OSCデータの送信準備が完了しました。送信先: {targetIP}:{port}");
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= sendInterval)
        {
            timer = 0f;
            // 時間経過で値がうねうね変わるようにして、動いているのが視覚的に分かりやすくする
            float time = Time.time;
            float headPitch = Mathf.Sin(time * 3f) * 35f; // 前後にうなずく（X軸）
            float headPan = Mathf.Cos(time * 2f) * 45f;   // 左右に首を振る（Y軸）
            float footPitch = Mathf.Cos(time * 3f) * 30f; // 足のパタパタ
            float armPitch = Mathf.Sin(time * 2f) * 45f;  // 腕のパタパタ

            // テスト用に頭・足・腕のトラッカーのアドレスに、ダミーの回転値(Euler角)を送信
            SendDummyData("/tracking/vrsystem/head/pose", headPitch, headPan, 0f);
            SendDummyData("/tracking/vrsystem/leftfoot/pose", footPitch, 0f, 0f);
            SendDummyData("/tracking/vrsystem/rightfoot/pose", -footPitch, 0f, 0f);
            SendDummyData("/tracking/vrsystem/leftarm/pose", 0f, 0f, armPitch);
            SendDummyData("/tracking/vrsystem/rightarm/pose", 0f, 0f, -armPitch);
        }
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
