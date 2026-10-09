using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    [Tooltip("ランダムに出現させる壁のプレハブをここに入れます")]
    public GameObject[] wallPrefabs; 
    public TextMeshProUGUI scoreText; // スコア表示用のUIテキスト
    public float spawnInterval = 3f;
    public Vector3 spawnPosition = new Vector3(0, 1, 15); // Z=15（奥）から壁を出す
    
    [Header("演出（エフェクト・サウンド）")]
    public AudioClip successSE;
    public AudioClip failSE;
    public ParticleSystem successParticle;
    private AudioSource audioSource;
    [Header("ゲームルール")]
    public int maxWalls = 10;
    public TextMeshProUGUI comboText;
    public GameObject resultPanel;
    
    private float timer = 0f;
    private int score = 0;
    private int combo = 0;
    private int spawnedWallsCount = 0;
    private bool isGameClear = false;

    void Start()
    {
        // タイトル画面で設定された難易度（出現間隔）を適用
        spawnInterval = GameSettings.wallSpawnInterval;

        // 音を鳴らすためのコンポーネントをアタッチ
        audioSource = gameObject.AddComponent<AudioSource>();
    }

    void Update()
    {
        if (isGameClear) return;
        if (wallPrefabs == null || wallPrefabs.Length == 0) return;

        timer += Time.deltaTime;
        if (timer >= spawnInterval && spawnedWallsCount < maxWalls)
        {
            SpawnWall();
            timer = 0f;
        }

        // 全ての壁を出し切り、画面上から壁がなくなったらクリア
        if (spawnedWallsCount >= maxWalls && FindObjectOfType<WallMover>() == null)
        {
            GameClear();
        }
    }

    void SpawnWall()
    {
        // ランダムな壁プレハブを選ぶ
        int randomIndex = Random.Range(0, wallPrefabs.Length);
        GameObject newWall = Instantiate(wallPrefabs[randomIndex], spawnPosition, Quaternion.identity);
        spawnedWallsCount++;
        
        // テスト用：壁の色を正解のポーズの色に合わせる（0=白, 1=赤, 2=青）
        WallMover mover = newWall.GetComponent<WallMover>();
        Renderer r = newWall.GetComponent<Renderer>();
        
        if (r != null && mover != null)
        {
            // インスタンスごとに色を変えるためマテリアルを複製
            r.material = new Material(r.material); 
            
            if (mover.requiredPose == 0) r.material.color = Color.white;
            else if (mover.requiredPose == 1) r.material.color = Color.red;
            else if (mover.requiredPose == 2) r.material.color = Color.blue;
        }
    }

    public void AddScore()
    {
        combo++;
        score += combo; // コンボ数分だけスコア加算
        
        if (scoreText != null) 
        {
            scoreText.text = $"Score: {score}";
        }
        if (comboText != null)
        {
            comboText.text = $"{combo} COMBO!";
        }

        // 成功時の演出
        if (audioSource != null && successSE != null) audioSource.PlayOneShot(successSE);
        if (successParticle != null) successParticle.Play();

        Debug.Log($"現在の成功回数: {score}回！");
    }

    public void Fail()
    {
        // 失敗時の演出
        combo = 0;
        if (comboText != null) comboText.text = "";

        if (audioSource != null && failSE != null) audioSource.PlayOneShot(failSE);
        Debug.Log("❌ 失敗...");
    }

    void GameClear()
    {
        isGameClear = true;
        if (resultPanel != null) resultPanel.SetActive(true);
        Debug.Log($"ゲームクリア！ 最終スコア: {score}");
    }
}
