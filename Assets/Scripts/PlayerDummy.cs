using UnityEngine;

public class PlayerDummy : MonoBehaviour
{
    [Header("現在のポーズ状態")]
    [Tooltip("0: 棒立ち, 1: バンザイ, 2: しゃがみ")]
    public int currentPose = 0;
    public string currentPoseName = "棒立ち (Stand)";

    [Header("ボーン参照")]
    public Transform headBone;
    public Transform spine;
    public Transform leftArmBone;
    public Transform rightArmBone;
    public Transform leftFootBone;
    public Transform rightFootBone;

    [Header("判定設定")]
    [Tooltip("ボーンの動きから自動的にポーズを判定する")]
    public bool autoDetectPose = true;
    [Tooltip("バンザイ判定：手が肩よりどれくらい高ければバンザイとするか（メートル）")]
    public float banzaiHeightThreshold = 0.15f;
    [Tooltip("しゃがみ判定：頭が基準位置よりどれくらい低くなればしゃがみとするか（メートル）")]
    public float squatDropThreshold = 0.25f;
    [Tooltip("しゃがみ判定：足首/脚の角度がどれくらい曲がればしゃがみとするか（度）")]
    public float squatFootAngleThreshold = 25f;
    [Tooltip("チラつき防止：同じポーズを維持する必要がある時間（秒）")]
    public float poseStabilityDuration = 0.1f;

    [Header("見た目のフィードバック")]
    public Renderer[] characterRenderers;

    private float initialHeadHeight = 1.55f;
    private bool isCalibrated = false;
    private int pendingPose = 0;
    private float pendingPoseTimer = 0f;

    void Start()
    {
        // ボーンが未割り当ての場合は自動探索
        AutoFindBones();

        // 人形全体のRendererを取得
        if (characterRenderers == null || characterRenderers.Length == 0)
        {
            characterRenderers = GetComponentsInChildren<Renderer>();
        }

        // 初期姿勢の頭の高さを記録（しゃがみ判定の基準値）
        if (headBone != null)
        {
            initialHeadHeight = headBone.position.y;
            isCalibrated = true;
        }

        ApplyPoseColor(0);
    }

    void Update()
    {
        // 手動キーボード操作（デバッグ用：1=棒立ち, 2=バンザイ, 3=しゃがみ）
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            autoDetectPose = false;
            ChangePose(0);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            autoDetectPose = false;
            ChangePose(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            autoDetectPose = false;
            ChangePose(2);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            // 0キーで自動判定モードに復帰
            autoDetectPose = true;
            Debug.Log("[Player] 自動ポーズ判定モードに戻りました");
        }

        // ボーンからの自動ポーズ判定
        if (autoDetectPose)
        {
            DetectPoseFromBones();
        }
    }

    void DetectPoseFromBones()
    {
        if (headBone == null) return;

        // 初期キャリブレーション（初期値が0付近だった場合の再取得）
        if (!isCalibrated && headBone.position.y > 0.5f)
        {
            initialHeadHeight = headBone.position.y;
            isCalibrated = true;
        }

        int detectedPose = 0; // デフォルトは 0: 棒立ち

        // --- 1. しゃがみ（Squat）の判定 ---
        // A. 頭の高さが下がっているか（位置トラッキングがある場合）
        float headDrop = initialHeadHeight - headBone.position.y;
        bool isHeadLow = headDrop >= squatDropThreshold;

        // B. 足首/脚が前後に大きく曲がっているか（回転トラッキングの場合）
        bool isFootBent = false;
        if (leftFootBone != null && rightFootBone != null)
        {
            float lFootAngle = Mathf.Abs(NormalizeAngle(leftFootBone.localEulerAngles.x));
            float rFootAngle = Mathf.Abs(NormalizeAngle(rightFootBone.localEulerAngles.x));
            isFootBent = (lFootAngle >= squatFootAngleThreshold && rFootAngle >= squatFootAngleThreshold);
        }

        if (isHeadLow || isFootBent)
        {
            detectedPose = 2; // しゃがみ
        }
        // --- 2. バンザイ（Banzai）の判定 ---
        else if (leftArmBone != null && rightArmBone != null)
        {
            // 腕の先端（手首付近）のワールド座標を計算
            // 左腕は-X方向、右腕は+X方向に伸びている
            Vector3 leftHandPos = leftArmBone.TransformPoint(new Vector3(-0.5f, 0, 0));
            Vector3 rightHandPos = rightArmBone.TransformPoint(new Vector3(0.5f, 0, 0));

            // 両手がそれぞれの肩よりも高い位置にあるか
            bool leftHandUp = leftHandPos.y > (leftArmBone.position.y + banzaiHeightThreshold);
            bool rightHandUp = rightHandPos.y > (rightArmBone.position.y + banzaiHeightThreshold);

            if (leftHandUp && rightHandUp)
            {
                detectedPose = 1; // バンザイ
            }
        }

        // チラつき防止：検出されたポーズが一定時間（poseStabilityDuration秒）安定したら確定
        if (detectedPose != currentPose)
        {
            if (detectedPose == pendingPose)
            {
                pendingPoseTimer += Time.deltaTime;
                if (pendingPoseTimer >= poseStabilityDuration)
                {
                    ChangePose(detectedPose);
                    pendingPoseTimer = 0f;
                }
            }
            else
            {
                pendingPose = detectedPose;
                pendingPoseTimer = 0f;
            }
        }
        else
        {
            pendingPose = currentPose;
            pendingPoseTimer = 0f;
        }
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    public void ChangePose(int newPose)
    {
        currentPose = newPose;
        switch (currentPose)
        {
            case 0:
                currentPoseName = "STAND";
                ApplyPoseColor(Color.white);
                break;
            case 1:
                currentPoseName = "BANZAI";
                ApplyPoseColor(Color.red);
                break;
            case 2:
                currentPoseName = "SQUAT";
                ApplyPoseColor(Color.blue);
                break;
        }

        Debug.Log($"[PoseDetector] Pose Detected: {currentPoseName} (ID: {currentPose})");
    }

    void ApplyPoseColor(int poseId)
    {
        if (poseId == 0) ApplyPoseColor(Color.white);
        else if (poseId == 1) ApplyPoseColor(Color.red);
        else if (poseId == 2) ApplyPoseColor(Color.blue);
    }

    void ApplyPoseColor(Color color)
    {
        if (characterRenderers == null) return;

        foreach (var r in characterRenderers)
        {
            if (r != null && r.gameObject.name != "Nose_Mesh") // 赤い鼻以外の色を変える
            {
                r.material.color = color;
            }
        }
    }

    private void AutoFindBones()
    {
        if (headBone == null)
        {
            Transform t = transform.Find("Spine/HeadBone");
            if (t != null) headBone = t;
        }
        if (spine == null)
        {
            Transform t = transform.Find("Spine");
            if (t != null) spine = t;
        }
        if (leftArmBone == null)
        {
            Transform t = transform.Find("Spine/LeftArmBone");
            if (t != null) leftArmBone = t;
        }
        if (rightArmBone == null)
        {
            Transform t = transform.Find("Spine/RightArmBone");
            if (t != null) rightArmBone = t;
        }
        if (leftFootBone == null)
        {
            Transform t = transform.Find("LeftHipBone/LeftFootBone");
            if (t != null) leftFootBone = t;
        }
        if (rightFootBone == null)
        {
            Transform t = transform.Find("RightHipBone/RightFootBone");
            if (t != null) rightFootBone = t;
        }
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 20;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;

        if (currentPose == 1) style.normal.textColor = Color.red;
        else if (currentPose == 2) style.normal.textColor = Color.cyan;
        else style.normal.textColor = Color.white;

        GUI.Box(new Rect(Screen.width - 270, 10, 260, 50), $"POSE: {currentPoseName}", style);
    }
}

