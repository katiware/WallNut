using UnityEngine;
using System.Net.Sockets;
using System.Text;

public class SlimeVRMockSender : MonoBehaviour
{
    [Tooltip("送信先のポート番号（SlimeVRTestReceiverと同じポート）")]
    public int port = 9000;
    [Tooltip("送信先のIPアドレス（自分自身の場合は 127.0.0.1）")]
    public string targetIP = "127.0.0.1";
    [Tooltip("送信間隔（秒）")]
    public float sendInterval = 1f;

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
            // テスト用に頭と足のトラッカーのアドレスを模倣して送信
            SendDummyData("/tracking/vrsystem/head/pose");
            SendDummyData("/tracking/vrsystem/leftfoot/pose");
            SendDummyData("/tracking/vrsystem/rightfoot/pose");
        }
    }

    void SendDummyData(string address)
    {
        try
        {
            // OSCの通信フォーマットは「アドレス文字列 + ヌル文字(\0) + 型情報 + データ」です。
            // 現在のSlimeVRTestReceiverは最初のアドレス部分だけを読み取っているため、
            // 簡易的にアドレスの後ろにヌル文字を付けたデータを送信します。
            string oscData = address + "\0,fff\0\0\0\0" + "dummy_bytes";
            byte[] data = Encoding.ASCII.GetBytes(oscData);
            
            udpClient.Send(data, data.Length, targetIP, port);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Mock Sender] 送信エラー: {e.Message}");
        }
    }

    void OnDestroy()
    {
        if (udpClient != null)
        {
            udpClient.Close();
        }
    }
}
