using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("ターゲット参照（自動取得）")]
    public Transform targetPlayer;

    [Header("カメラ推奨固定位置（デフォルト）")]
    public Vector3 defaultPosition = new Vector3(0f, 1.7f, -4.2f);
    public Vector3 defaultRotation = new Vector3(6f, 0f, 0f);
    public float defaultFOV = 60f;

    [Header("現在のカメラ設定")]
    public float cameraHeight = 1.7f;
    public float cameraDistance = -4.2f;
    public float cameraTilt = 6f;
    public float cameraFOV = 60f;

    [Header("調整モード設定")]
    [Tooltip("起動時・プレイ前に調整UIを表示するか")]
    public bool showAdjustmentUI = false;
    [Tooltip("位置を固定する（外部からの変動を防止）")]
    public bool isLocked = true;

    private Camera cam;
    private const string PREF_KEY_HEIGHT = "WallNut_Cam_Height";
    private const string PREF_KEY_DIST = "WallNut_Cam_Dist";
    private const string PREF_KEY_TILT = "WallNut_Cam_Tilt";
    private const string PREF_KEY_FOV = "WallNut_Cam_FOV";

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;

        // 保存された設定があればロード、無ければデフォルト値を適用
        LoadCameraSettings();

        // 起動時に直ちに位置を固定
        ApplyCameraTransform();
    }

    void Start()
    {
        if (targetPlayer == null)
        {
            PlayerDummy dummy = FindObjectOfType<PlayerDummy>();
            if (dummy != null) targetPlayer = dummy.transform;
        }

        // 初期位置を固定適用
        ApplyCameraTransform();
    }

    void Update()
    {
        // F1キーでカメラ調整UIの開閉
        if (Input.GetKeyDown(KeyCode.F1))
        {
            showAdjustmentUI = !showAdjustmentUI;
        }

        // 調整UIが開いているときはキーボードでも微調整可能
        if (showAdjustmentUI)
        {
            // 上下（高さ）: テンキー8/2 または PageUp/PageDown
            if (Input.GetKey(KeyCode.PageUp)) { cameraHeight += Time.deltaTime * 1.5f; ApplyCameraTransform(); }
            if (Input.GetKey(KeyCode.PageDown)) { cameraHeight -= Time.deltaTime * 1.5f; ApplyCameraTransform(); }

            // 前後（距離）: 上下矢印キー
            if (Input.GetKey(KeyCode.UpArrow)) { cameraDistance += Time.deltaTime * 2.0f; ApplyCameraTransform(); }
            if (Input.GetKey(KeyCode.DownArrow)) { cameraDistance -= Time.deltaTime * 2.0f; ApplyCameraTransform(); }
        }
    }

    void LateUpdate()
    {
        // 位置がロックされている場合は固定値を常に維持（物理揺れやズレを遮断）
        if (isLocked && !showAdjustmentUI)
        {
            ApplyCameraTransform();
        }
    }

    /// <summary>
    /// 設定値をカメラのTransformとFOVへ即座に反映して固定する
    /// </summary>
    public void ApplyCameraTransform()
    {
        float targetX = (targetPlayer != null) ? targetPlayer.position.x : 0f;
        transform.position = new Vector3(targetX, cameraHeight, cameraDistance);
        transform.rotation = Quaternion.Euler(cameraTilt, 0f, 0f);

        if (cam != null)
        {
            cam.fieldOfView = cameraFOV;
        }
    }

    /// <summary>
    /// 現在のカメラ位置を保存して固定する
    /// </summary>
    public void SaveAndLock()
    {
        PlayerPrefs.SetFloat(PREF_KEY_HEIGHT, cameraHeight);
        PlayerPrefs.SetFloat(PREF_KEY_DIST, cameraDistance);
        PlayerPrefs.SetFloat(PREF_KEY_TILT, cameraTilt);
        PlayerPrefs.SetFloat(PREF_KEY_FOV, cameraFOV);
        PlayerPrefs.Save();

        isLocked = true;
        showAdjustmentUI = false;
        ApplyCameraTransform();

        Debug.Log($"📷 [CameraController] カメラ位置を固定＆保存しました: Pos=(0, {cameraHeight:F2}, {cameraDistance:F2}), Tilt={cameraTilt:F1}°, FOV={cameraFOV:F1}");
    }

    /// <summary>
    /// デフォルトの推奨位置へリセット
    /// </summary>
    public void ResetToDefault()
    {
        cameraHeight = defaultPosition.y;
        cameraDistance = defaultPosition.z;
        cameraTilt = defaultRotation.x;
        cameraFOV = defaultFOV;

        ApplyCameraTransform();
        SaveAndLock();
        Debug.Log("📷 [CameraController] カメラを推奨デフォルト位置へリセットしました。");
    }

    private void LoadCameraSettings()
    {
        cameraHeight = PlayerPrefs.GetFloat(PREF_KEY_HEIGHT, defaultPosition.y);
        cameraDistance = PlayerPrefs.GetFloat(PREF_KEY_DIST, defaultPosition.z);
        cameraTilt = PlayerPrefs.GetFloat(PREF_KEY_TILT, defaultRotation.x);
        cameraFOV = PlayerPrefs.GetFloat(PREF_KEY_FOV, defaultFOV);
    }

    void OnGUI()
    {
        // 画面上部にカメラ調整を開くミニボタン（調整UIが閉じていてタイトル画面等の場合）
        if (!showAdjustmentUI)
        {
            if (GUI.Button(new Rect(Screen.width - 150, Screen.height - 40, 140, 30), "📷 カメラ調整 (F1)"))
            {
                showAdjustmentUI = true;
            }
            return;
        }

        // --- カメラ微調整パネル ---
        GUILayout.BeginArea(new Rect(Screen.width - 340, 70, 330, 360), "【カメラ位置・視角の調整＆固定】", GUI.skin.window);

        GUILayout.Label($"高さ (Y): {cameraHeight:F2} m");
        float newHeight = GUILayout.HorizontalSlider(cameraHeight, 0.5f, 3.5f);
        if (Mathf.Abs(newHeight - cameraHeight) > 0.001f) { cameraHeight = newHeight; ApplyCameraTransform(); }

        GUILayout.Space(10);
        GUILayout.Label($"距離 (前後 Z): {cameraDistance:F2} m");
        float newDist = GUILayout.HorizontalSlider(cameraDistance, -7.0f, -2.0f);
        if (Mathf.Abs(newDist - cameraDistance) > 0.001f) { cameraDistance = newDist; ApplyCameraTransform(); }

        GUILayout.Space(10);
        GUILayout.Label($"角度 (見下ろし Tilt): {cameraTilt:F1} °");
        float newTilt = GUILayout.HorizontalSlider(cameraTilt, -10f, 25f);
        if (Mathf.Abs(newTilt - cameraTilt) > 0.001f) { cameraTilt = newTilt; ApplyCameraTransform(); }

        GUILayout.Space(10);
        GUILayout.Label($"視野角 (FOV): {cameraFOV:F0} °");
        float newFOV = GUILayout.HorizontalSlider(cameraFOV, 40f, 90f);
        if (Mathf.Abs(newFOV - cameraFOV) > 0.001f) { cameraFOV = newFOV; ApplyCameraTransform(); }

        GUILayout.Space(15);
        if (GUILayout.Button("🔒 この位置で固定・保存 (Lock & Save)", GUILayout.Height(40)))
        {
            SaveAndLock();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("初期値にリセット"))
        {
            ResetToDefault();
        }
        if (GUILayout.Button("閉じる [F1]"))
        {
            showAdjustmentUI = false;
        }
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}
