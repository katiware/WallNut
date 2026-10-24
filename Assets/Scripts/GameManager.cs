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
    [Header("UIパネル・HUD参照（大画面プロジェクター用）")]
    public GameObject titlePanel;
    public GameObject countdownPanel;
    public TextMeshProUGUI countdownText;
    public GameObject inGameHUD;
    public TextMeshProUGUI progressText;
    public GameObject resultPanel;
    public TextMeshProUGUI resultStatsText;
    public TextMeshProUGUI resultRankText;

    [Header("ゲームルール")]
    public int maxWalls = 10;
    public TextMeshProUGUI comboText;

    public enum GameState { Title, Countdown, Playing, Result }
    [Header("現在のゲーム状態")]
    public GameState currentState = GameState.Title;

    private float timer = 0f;
    private int score = 0;
    private int successCount = 0; // 純粋な成功回数（壁1枚につき+1）
    private int combo = 0;
    private int spawnedWallsCount = 0;
    private Coroutine countdownCoroutine;

    void Start()
    {
        // 音を鳴らすためのコンポーネントをアタッチ
        audioSource = gameObject.AddComponent<AudioSource>();

        // カメラが未設定の場合は自動でCameraControllerをアタッチして最適位置に固定
        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.GetComponent<CameraController>() == null)
        {
            mainCam.gameObject.AddComponent<CameraController>();
        }

        // 起動時はタイトル画面から開始
        ShowTitle();
    }

    void Update()
    {
        if (currentState != GameState.Playing) return;
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

    public void ShowTitle()
    {
        currentState = GameState.Title;
        ClearActiveWalls();

        if (titlePanel != null) titlePanel.SetActive(true);
        if (countdownPanel != null) countdownPanel.SetActive(false);
        if (inGameHUD != null) inGameHUD.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);
    }

    public void SelectDifficultyEasy()
    {
        GameSettings.wallSpeed = 3.5f;
        GameSettings.wallSpawnInterval = 4.0f;
        StartCountdown();
    }

    public void SelectDifficultyNormal()
    {
        GameSettings.wallSpeed = 5.0f;
        GameSettings.wallSpawnInterval = 3.0f;
        StartCountdown();
    }

    public void SelectDifficultyHard()
    {
        GameSettings.wallSpeed = 7.5f;
        GameSettings.wallSpawnInterval = 2.0f;
        StartCountdown();
    }

    public void StartCountdown()
    {
        spawnInterval = GameSettings.wallSpawnInterval;
        ClearActiveWalls();

        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        countdownCoroutine = StartCoroutine(CountdownRoutine());
    }

    private System.Collections.IEnumerator CountdownRoutine()
    {
        currentState = GameState.Countdown;

        if (titlePanel != null) titlePanel.SetActive(false);
        if (countdownPanel != null) countdownPanel.SetActive(true);
        if (inGameHUD != null) inGameHUD.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);

        for (int i = 3; i > 0; i--)
        {
            if (countdownText != null) countdownText.text = i.ToString();
            Debug.Log($"⏳ カウントダウン: {i}");
            yield return new WaitForSeconds(1.0f);
        }

        if (countdownText != null) countdownText.text = "START!";
        yield return new WaitForSeconds(0.6f);

        StartGamePlaying();
    }

    private void StartGamePlaying()
    {
        currentState = GameState.Playing;
        score = 0;
        successCount = 0;
        combo = 0;
        spawnedWallsCount = 0;
        timer = spawnInterval * 0.5f; // 最初は少し早めに第1問を出す

        if (countdownPanel != null) countdownPanel.SetActive(false);
        if (inGameHUD != null) inGameHUD.SetActive(true);

        UpdateUI();
        Debug.Log("🏁 ゲームスタート！ 壁が迫ってきます！");
    }

    void SpawnWall()
    {
        int targetPose = Random.Range(0, 7); // 0〜6の全ポーズからランダム抽選
        GameObject newWall;

        if (wallPrefabs != null && wallPrefabs.Length > 0)
        {
            int randomIndex = Random.Range(0, wallPrefabs.Length);
            newWall = Instantiate(wallPrefabs[randomIndex], spawnPosition, Quaternion.identity);
            WallMover mover = newWall.GetComponent<WallMover>();
            if (mover != null)
            {
                mover.requiredPose = targetPose; // 全バリエーションのポーズを適用
            }
        }
        else
        {
            // プレハブ未登録時の安全な自動生成フォールバック
            newWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            newWall.name = $"Wall_{targetPose}";
            newWall.transform.position = spawnPosition;
            WallMover mover = newWall.AddComponent<WallMover>();
            mover.requiredPose = targetPose;
            newWall.AddComponent<WallBuilder>();
        }

        spawnedWallsCount++;
        
        WallMover wallMover = newWall.GetComponent<WallMover>();
        Renderer r = newWall.GetComponent<Renderer>();
        
        if (r != null && wallMover != null)
        {
            r.material = new Material(r.material); 
            r.material.color = GetWallColor(wallMover.requiredPose);
        }

        UpdateUI();
    }

    public static Color GetWallColor(int poseId)
    {
        switch (poseId)
        {
            case 0: return Color.white;
            case 1: return Color.red;
            case 2: return new Color(0.2f, 0.6f, 1f); // 青
            case 3: return new Color(0.2f, 1f, 0.3f); // 緑
            case 4: return new Color(1f, 0.85f, 0.1f); // 黄
            case 5: return new Color(0.1f, 0.95f, 0.95f); // シアン
            case 6: return new Color(1f, 0.25f, 0.8f); // マゼンタ
            default: return Color.white;
        }
    }

    /// <summary>
    /// プレイヤーの身長・姿勢キャリブレーションを実行
    /// </summary>
    public void TriggerCalibration()
    {
        PlayerDummy dummy = FindObjectOfType<PlayerDummy>();
        if (dummy != null)
        {
            dummy.Calibrate();
        }
        else
        {
            Debug.LogWarning("[GameManager] PlayerDummyが見つかりませんでした。");
        }
    }

    public void AddScore()
    {
        successCount++;
        combo++;
        score += combo * 100;

        UpdateUI();

        if (audioSource != null && successSE != null) audioSource.PlayOneShot(successSE);
        if (successParticle != null) successParticle.Play();

        Debug.Log($"⭕ 成功！ ({successCount}/{maxWalls}回) Score: {score}, {combo} COMBO!");
    }

    public void Fail()
    {
        combo = 0;
        UpdateUI();

        if (audioSource != null && failSE != null) audioSource.PlayOneShot(failSE);
        Debug.Log($"❌ 失敗... ({successCount}/{maxWalls}回)");
    }

    void UpdateUI()
    {
        if (scoreText != null) 
        {
            scoreText.text = $"SCORE: {score}";
        }
        if (progressText != null)
        {
            progressText.text = $"WALL: {Mathf.Min(spawnedWallsCount, maxWalls)} / {maxWalls}";
        }
        if (comboText != null)
        {
            comboText.text = combo > 1 ? $"{combo} COMBO!" : "";
        }
    }

    void GameClear()
    {
        currentState = GameState.Result;
        if (inGameHUD != null) inGameHUD.SetActive(false);

        string rank = "C";
        if (successCount >= maxWalls) rank = "S+";
        else if (successCount >= 8) rank = "S";
        else if (successCount >= 6) rank = "A";
        else if (successCount >= 4) rank = "B";

        if (resultPanel != null)
        {
            resultPanel.SetActive(true);
            if (resultRankText != null) resultRankText.text = $"RANK: {rank}";
            if (resultStatsText != null)
            {
                resultStatsText.text = $"PASSED: {successCount} / {maxWalls}\nFINAL SCORE: {score:N0}";
            }
        }

        Debug.Log($"🎉 ゲーム終了！ ランク: {rank} 成功回数: {successCount}/{maxWalls}回 (最終スコア: {score})");
    }

    public void ClearActiveWalls()
    {
        WallMover[] activeWalls = FindObjectsOfType<WallMover>();
        foreach (var wall in activeWalls)
        {
            Destroy(wall.gameObject);
        }
    }

    void OnGUI()
    {
        // Canvasが未配置の場合のフォールバック＆プロジェクター用クイック操作
        if (titlePanel == null)
        {
            DrawFallbackGUI();
        }
    }

    private void DrawFallbackGUI()
    {
        if (currentState == GameState.Title)
        {
            GUILayout.BeginArea(new Rect(Screen.width * 0.25f, Screen.height * 0.15f, Screen.width * 0.5f, Screen.height * 0.7f), "【WallNut タイトル】", GUI.skin.window);
            GUILayout.Label("全身ポーズアクション WallNut", GUI.skin.box);
            GUILayout.Space(15);
            if (GUILayout.Button("🎯 身長・姿勢キャリブレーション [Cキー]", GUILayout.Height(45))) TriggerCalibration();
            GUILayout.Space(15);
            GUILayout.Label("▼ 難易度を選択してスタート ▼");
            if (GUILayout.Button("🟢 かんたん (Easy: ゆっくり)", GUILayout.Height(50))) SelectDifficultyEasy();
            if (GUILayout.Button("🟡 ふつう (Normal: 標準)", GUILayout.Height(50))) SelectDifficultyNormal();
            if (GUILayout.Button("🔴 むずかしい (Hard: 高速)", GUILayout.Height(50))) SelectDifficultyHard();
            GUILayout.EndArea();
        }
        else if (currentState == GameState.Countdown)
        {
            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.fontSize = 50;
            style.alignment = TextAnchor.MiddleCenter;
            GUI.Box(new Rect(Screen.width * 0.3f, Screen.height * 0.35f, Screen.width * 0.4f, 120), "READY...", style);
        }
        else if (currentState == GameState.Playing)
        {
            GUIStyle hudStyle = new GUIStyle(GUI.skin.box);
            hudStyle.fontSize = 24;
            hudStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Box(new Rect(Screen.width * 0.35f, 10, Screen.width * 0.3f, 50), $"WALL: {Mathf.Min(spawnedWallsCount, maxWalls)} / {maxWalls}   SCORE: {score}", hudStyle);
        }
        else if (currentState == GameState.Result)
        {
            GUILayout.BeginArea(new Rect(Screen.width * 0.25f, Screen.height * 0.15f, Screen.width * 0.5f, Screen.height * 0.7f), "【GAME RESULT】", GUI.skin.window);
            GUILayout.Label($"★ 結果発表 ★\n成功回数: {successCount} / {maxWalls}\nスコア: {score}", GUI.skin.box);
            GUILayout.Space(15);
            if (GUILayout.Button("🎯 身長・姿勢キャリブレーション [Cキー]", GUILayout.Height(45))) TriggerCalibration();
            GUILayout.Space(15);
            if (GUILayout.Button("もう一度あそぶ (Retry)", GUILayout.Height(50))) StartCountdown();
            if (GUILayout.Button("タイトルへもどる (Title)", GUILayout.Height(50))) ShowTitle();
            GUILayout.EndArea();
        }
    }
}
