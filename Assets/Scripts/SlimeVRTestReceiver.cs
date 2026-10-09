using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class SlimeVRTestReceiver : MonoBehaviour
{
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = false;

    [Tooltip("SlimeVRがOSCを送信するポート。デフォルトは9000です")]
    public int port = 9000; 

    void Start()
    {
        StartReceiving();
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
            Debug.LogError($"[SlimeVR Test] ポートのバインドに失敗しました。他のアプリ(VRChatなど)がポート {port} を使用していないか確認してください。\n{e.Message}");
        }
    }

    private void ReceiveData()
    {
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
        while (isRunning)
        {
            try
            {
                // データを受信
                byte[] data = udpClient.Receive(ref anyIP);
                
                // OSC形式は「文字列(アドレス) + \0 + 文字列(型) + バイナリデータ(数値)」になっています
                // まずは通信が来ているか確認するため、先頭のアドレス部分だけを抽出してログに出します
                string text = Encoding.ASCII.GetString(data);
                int nullCharIndex = text.IndexOf('\0');
                
                if (nullCharIndex > 0)
                {
                    string address = text.Substring(0, nullCharIndex);
                    // ログが大量に出すぎるのを防ぐため、特定のトラッカーのログのみを出すか、間引きしたほうが良いですが
                    // 今回はテストなのでそのまま出力します（重くなる場合はUnity側でCollapseを有効にしてください）
                    Debug.Log($"[SlimeVR Test] 受信: {address} (サイズ: {data.Length} bytes)");
                }
            }
            catch (SocketException)
            {
                // クローズ時の例外などは無視
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(e.ToString());
            }
        }
    }

    void OnDestroy()
    {
        isRunning = false;
        if (udpClient != null)
        {
            udpClient.Close();
        }
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(500);
        }
    }
}
