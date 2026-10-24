using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

public class ProjectorUIBuilder : EditorWindow
{
    [MenuItem("Tools/Build Projector UI (大画面UI構築)")]
    public static void BuildUI()
    {
        GameManager gm = FindObjectOfType<GameManager>();
        if (gm == null)
        {
            Debug.LogError("❌ シーン内にGameManagerが見つかりません。先にGameManagerを配置してください。");
            return;
        }

        // 既存のGameCanvasがあれば削除して再構築
        GameObject existingCanvas = GameObject.Find("GameCanvas");
        if (existingCanvas != null)
        {
            Undo.DestroyObjectImmediate(existingCanvas);
        }

        // EventSystemの確認・作成
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        }

        // --- 1. Canvas作成 (1920x1080 プロジェクター基準) ---
        GameObject canvasObj = new GameObject("GameCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        Undo.RegisterCreatedObjectUndo(canvasObj, "Create GameCanvas");
        Undo.RecordObject(gm, "Assign UI to GameManager");

        // --- 2. Title Panel (タイトル画面) ---
        GameObject titlePanel = CreatePanel("TitlePanel", canvasObj.transform, new Color(0, 0, 0, 0.8f));
        CreateText("TitleText", titlePanel.transform, "WallNut", 100, FontStyles.Bold, new Vector2(0, 260), new Vector2(1200, 150), Color.yellow);
        CreateText("SubtitleText", titlePanel.transform, "BODY POSE ACTION GAME", 44, FontStyles.Normal, new Vector2(0, 160), new Vector2(1200, 70), Color.white);
        
        CreateButton("Button_Calibrate", titlePanel.transform, "🎯 CALIBRATE POSTURE [C]", new Vector2(0, 50), new Vector2(500, 75), 28, gm, nameof(GameManager.TriggerCalibration));
        CreateText("DescText", titlePanel.transform, "- SELECT DIFFICULTY TO START -", 34, FontStyles.Normal, new Vector2(0, -40), new Vector2(1000, 50), new Color(0.8f, 0.8f, 0.8f));

        CreateButton("Button_Easy", titlePanel.transform, "EASY (Slow)", new Vector2(-420, -140), new Vector2(360, 95), 30, gm, nameof(GameManager.SelectDifficultyEasy));
        CreateButton("Button_Normal", titlePanel.transform, "NORMAL", new Vector2(0, -140), new Vector2(360, 95), 30, gm, nameof(GameManager.SelectDifficultyNormal));
        CreateButton("Button_Hard", titlePanel.transform, "HARD (Fast)", new Vector2(420, -140), new Vector2(360, 95), 30, gm, nameof(GameManager.SelectDifficultyHard));

        // --- 3. Countdown Panel (カウントダウン画面) ---
        GameObject countdownPanel = CreatePanel("CountdownPanel", canvasObj.transform, new Color(0, 0, 0, 0.5f));
        TextMeshProUGUI countdownText = CreateText("CountdownText", countdownPanel.transform, "3", 160, FontStyles.Bold, Vector2.zero, new Vector2(800, 300), Color.yellow);

        // --- 4. InGame HUD (プレイ中画面) ---
        GameObject inGameHUD = CreatePanel("InGameHUD", canvasObj.transform, Color.clear);
        TextMeshProUGUI progressText = CreateText("ProgressText", inGameHUD.transform, "WALL: 1 / 10", 44, FontStyles.Bold, new Vector2(0, 460), new Vector2(600, 80), Color.white);
        TextMeshProUGUI scoreText = CreateText("ScoreText", inGameHUD.transform, "SCORE: 0", 40, FontStyles.Bold, new Vector2(650, 460), new Vector2(500, 80), Color.cyan);
        TextMeshProUGUI comboText = CreateText("ComboText", inGameHUD.transform, "", 48, FontStyles.Bold, new Vector2(0, 360), new Vector2(600, 90), Color.yellow);

        // --- 5. Result Panel (リザルト画面) ---
        GameObject resultPanel = CreatePanel("ResultPanel", canvasObj.transform, new Color(0, 0, 0, 0.88f));
        CreateText("ResultHeader", resultPanel.transform, "★ GAME CLEAR ★", 68, FontStyles.Bold, new Vector2(0, 320), new Vector2(1000, 100), Color.yellow);
        TextMeshProUGUI resultRankText = CreateText("RankText", resultPanel.transform, "RANK: S", 96, FontStyles.Bold, new Vector2(0, 190), new Vector2(800, 130), Color.green);
        TextMeshProUGUI resultStatsText = CreateText("StatsText", resultPanel.transform, "PASSED: 8 / 10\nFINAL SCORE: 3,200", 44, FontStyles.Normal, new Vector2(0, 40), new Vector2(900, 140), Color.white);

        CreateButton("Button_Retry", resultPanel.transform, "PLAY AGAIN (Retry)", new Vector2(-360, -180), new Vector2(330, 85), 28, gm, nameof(GameManager.StartCountdown));
        CreateButton("Button_Calibrate_Result", resultPanel.transform, "🎯 CALIBRATE [C]", new Vector2(0, -180), new Vector2(330, 85), 26, gm, nameof(GameManager.TriggerCalibration));
        CreateButton("Button_Title", resultPanel.transform, "TITLE SCREEN", new Vector2(360, -180), new Vector2(330, 85), 28, gm, nameof(GameManager.ShowTitle));

        // --- GameManagerへ自動アサイン ---
        gm.titlePanel = titlePanel;
        gm.countdownPanel = countdownPanel;
        gm.countdownText = countdownText;
        gm.inGameHUD = inGameHUD;
        gm.progressText = progressText;
        gm.scoreText = scoreText;
        gm.comboText = comboText;
        gm.resultPanel = resultPanel;
        gm.resultRankText = resultRankText;
        gm.resultStatsText = resultStatsText;

        // 初期表示設定
        titlePanel.SetActive(true);
        countdownPanel.SetActive(false);
        inGameHUD.SetActive(false);
        resultPanel.SetActive(false);

        Debug.Log("✨ プロジェクター用大画面UI（タイトル・カウントダウン・HUD・リザルト）を完全構築し、GameManagerへ接続しました！");
    }

    private static GameObject CreatePanel(string name, Transform parent, Color bgColor)
    {
        GameObject panelObj = new GameObject(name);
        panelObj.transform.SetParent(parent, false);

        RectTransform rt = panelObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = panelObj.AddComponent<Image>();
        img.color = bgColor;

        return panelObj;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize, FontStyles style, Vector2 pos, Vector2 size, Color color)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        RectTransform rt = textObj.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;

        return tmp;
    }

    private static GameObject CreateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, float fontSize, UnityEngine.Object target, string methodName)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = btnObj.AddComponent<Image>();
        img.color = new Color(0.18f, 0.22f, 0.32f, 0.95f); // 落ち着いたダークネイビー

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0.3f, 0.5f, 0.8f, 1f);
        cb.pressedColor = new Color(0.1f, 0.3f, 0.5f, 1f);
        btn.colors = cb;

        // ボタンテキスト
        CreateText(name + "_Text", btnObj.transform, label, fontSize, FontStyles.Bold, Vector2.zero, size, Color.white);

        // Persistent Listenerの登録（インスペクターに保存される正規のOnClickイベント）
        UnityAction action = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), target, methodName);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, action);

        return btnObj;
    }
}
