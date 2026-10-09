using UnityEngine;

public class WallMover : MonoBehaviour
{
    public float speed = 5f;
    [Tooltip("この壁を通り抜けるための正解のポーズ (0, 1, 2)")]
    public int requiredPose = 0; 
    
    private bool hasPassed = false;
    private PlayerDummy player;
    private GameManager gameManager;

    void Start()
    {
        player = FindObjectOfType<PlayerDummy>();
        gameManager = FindObjectOfType<GameManager>();

        // タイトル画面で設定された難易度（壁の速度）を適用
        speed = GameSettings.wallSpeed;
    }

    void Update()
    {
        // 壁を手前(-Z方向)に移動させる
        transform.position += Vector3.back * speed * Time.deltaTime;

        // プレイヤー(Z=0地点)を通り過ぎた瞬間に判定を行う
        if (transform.position.z <= 0 && !hasPassed)
        {
            hasPassed = true;
            CheckPose();
        }

        // カメラの後ろまで行ったらオブジェクトを削除
        if (transform.position.z < -10f)
        {
            Destroy(gameObject);
        }
    }

    void CheckPose()
    {
        if (player != null && gameManager != null)
        {
            if (player.currentPose == requiredPose)
            {
                Debug.Log("⭕ 成功！");
                gameManager.AddScore();
            }
            else
            {
                Debug.Log("❌ 失敗...");
            }
        }
    }
}
