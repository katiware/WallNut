using UnityEngine;

public class PlayerDummy : MonoBehaviour
{
    public enum PoseType
    {
        Stand = 0,       // 棒立ち (白)
        Banzai = 1,      // バンザイ (赤)
        Squat = 2,       // しゃがみ (青)
        TPose = 3,       // Tポーズ / 腕水平 (緑)
        RightArmUp = 4,  // 右腕上げ (黄)
        LeftArmUp = 5,   // 左腕上げ (シアン)
        Flamingo = 6     // 片足立ち / フラミンゴ (マゼンタ)
    }

    [Header("現在のポーズ状態")]
    public int currentPose = 0;
    public string currentPoseName = "STAND";

    [Header("ボーン参照（7点トラッキング対応）")]
    [Tooltip("胸・胴体 (1点)")]
    public Transform spine;
    [Tooltip("左手・左腕 (1点: 握りこみ)")]
    public Transform leftArmBone;
    [Tooltip("右手・右腕 (1点: 握りこみ)")]
    public Transform rightArmBone;
    [Tooltip("左膝 (1点)")]
    public Transform leftKneeBone;
    [Tooltip("右膝 (1点)")]
    public Transform rightKneeBone;
    [Tooltip("左足首 (1点)")]
    public Transform leftFootBone;
    [Tooltip("右足首 (1点)")]
    public Transform rightFootBone;
    [Tooltip("頭（任意・未装着でも動作可能）")]
    public Transform headBone;

    [Header("判定設定")]
    [Tooltip("ボーンの動きから自動的にポーズを判定する")]
    public bool autoDetectPose = true;
    [Tooltip("バンザイ判定：手が肩よりどれくらい高ければバンザイとするか（メートル）")]
    public float banzaiHeightThreshold = 0.15f;
    [Tooltip("腕下げ判定：手が肩よりどれくらい低ければ腕を下げているとするか（メートル）")]
    public float armDownThreshold = 0.10f;
    [Tooltip("Tポーズ判定：手の高さが肩の高さに対して許容される誤差（メートル）")]
    public float tPoseHeightTolerance = 0.18f;
    [Tooltip("Tポーズ判定：両手の横幅の最小スパン（メートル）")]
    public float tPoseWidthMinSpan = 0.8f;
    [Tooltip("しゃがみ判定：胸の高さが基準位置よりどれくらい低くなればしゃがみとするか（メートル）")]
    public float squatDropThreshold = 0.20f;
    [Tooltip("しゃがみ/片足判定：膝や足首の角度がどれくらい曲がれば曲がったとするか（度）")]
    public float legAngleThreshold = 25f;
    [Tooltip("チラつき防止：同じポーズを維持する必要がある時間（秒）")]
    public float poseStabilityDuration = 0.08f;

    [Header("キャリブレーション（姿勢・身長基準）")]
    public bool isCalibrated = false;
    public float calibratedSpineHeight = 1.0f;
    public float calibratedHeadHeight = 1.55f;
    public float calibratedLeftArmHeight = 1.2f;
    public float calibratedRightArmHeight = 1.2f;
    public Quaternion calibratedLeftFootRot = Quaternion.identity;
    public Quaternion calibratedRightFootRot = Quaternion.identity;
    public Quaternion calibratedLeftKneeRot = Quaternion.identity;
    public Quaternion calibratedRightKneeRot = Quaternion.identity;
    private string calibrationMessage = "";
    private float calibrationMessageTimer = 0f;

    [Header("見た目のフィードバック")]
    public Renderer[] characterRenderers;

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

        // 初期キャリブレーション
        Calibrate();
        ApplyPoseColor(0);
    }

    void Update()
    {
        // Cキーでいつでもワンタッチキャリブレーション
        if (Input.GetKeyDown(KeyCode.C))
        {
            Calibrate();
        }

        // 手動キーボード操作（デバッグ用）
        if (Input.GetKeyDown(KeyCode.Alpha1)) { autoDetectPose = false; ChangePose(0); } // 棒立ち
        else if (Input.GetKeyDown(KeyCode.Alpha2)) { autoDetectPose = false; ChangePose(1); } // バンザイ
        else if (Input.GetKeyDown(KeyCode.Alpha3)) { autoDetectPose = false; ChangePose(2); } // しゃがみ
        else if (Input.GetKeyDown(KeyCode.Alpha4)) { autoDetectPose = false; ChangePose(3); } // Tポーズ
        else if (Input.GetKeyDown(KeyCode.Alpha5)) { autoDetectPose = false; ChangePose(4); } // 右腕上げ
        else if (Input.GetKeyDown(KeyCode.Alpha6)) { autoDetectPose = false; ChangePose(5); } // 左腕上げ
        else if (Input.GetKeyDown(KeyCode.Alpha7)) { autoDetectPose = false; ChangePose(6); } // フラミンゴ
        else if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            autoDetectPose = true;
            Debug.Log("[Player] 自動ポーズ判定モードに戻りました");
        }

        // キャリブレーションメッセージの表示タイマー更新
        if (calibrationMessageTimer > 0f)
        {
            calibrationMessageTimer -= Time.deltaTime;
        }

        // ボーンからの自動ポーズ判定
        if (autoDetectPose)
        {
            DetectPoseFromBones();
        }
    }

    /// <summary>
    /// 現在の直立姿勢を基準として胸の高さ・肩の高さ・足や膝の角度を記録（キャリブレーション）
    /// </summary>
    public void Calibrate()
    {
        // 先にSlimeVR側の基準（トラッカー番号の割り当て・正面方向・基準回転）を確定し、人形をレスト姿勢に戻す
        SlimeVRTestReceiver receiver = FindObjectOfType<SlimeVRTestReceiver>();
        if (receiver != null) receiver.CalibrateTracking();

        if (spine != null)
        {
            calibratedSpineHeight = spine.position.y;
        }
        if (headBone != null)
        {
            calibratedHeadHeight = headBone.position.y;
        }
        if (leftArmBone != null)
        {
            calibratedLeftArmHeight = leftArmBone.position.y;
        }
        if (rightArmBone != null)
        {
            calibratedRightArmHeight = rightArmBone.position.y;
        }
        if (leftFootBone != null)
        {
            calibratedLeftFootRot = leftFootBone.localRotation;
        }
        if (rightFootBone != null)
        {
            calibratedRightFootRot = rightFootBone.localRotation;
        }
        if (leftKneeBone != null)
        {
            calibratedLeftKneeRot = leftKneeBone.localRotation;
        }
        if (rightKneeBone != null)
        {
            calibratedRightKneeRot = rightKneeBone.localRotation;
        }

        isCalibrated = true;
        calibrationMessage = $"[CALIBRATION OK] 基準胸高: {calibratedSpineHeight:F2}m";
        calibrationMessageTimer = 2.5f;
        Debug.Log($"[Calibration] {calibrationMessage} | LeftArm: {calibratedLeftArmHeight:F2}m, RightArm: {calibratedRightArmHeight:F2}m");
    }

    void DetectPoseFromBones()
    {
        // 基準ボーン（胸または足）の有無をチェック
        if (spine == null && leftFootBone == null) return;

        // 初期キャリブレーションフォールバック
        if (!isCalibrated && spine != null && spine.position.y > 0.3f)
        {
            Calibrate();
        }

        int detectedPose = 0; // デフォルトは 0: 棒立ち (Stand)

        // --- 1. 足首および膝の曲がり具合の計算（7点トラッキング: 3D回転角差分） ---
        bool isLeftLegBent = false;
        bool isRightLegBent = false;

        if (leftFootBone != null)
        {
            float lFootDelta = Quaternion.Angle(leftFootBone.localRotation, calibratedLeftFootRot);
            if (lFootDelta >= legAngleThreshold) isLeftLegBent = true;
        }
        if (rightFootBone != null)
        {
            float rFootDelta = Quaternion.Angle(rightFootBone.localRotation, calibratedRightFootRot);
            if (rFootDelta >= legAngleThreshold) isRightLegBent = true;
        }
        if (leftKneeBone != null)
        {
            float lKneeDelta = Quaternion.Angle(leftKneeBone.localRotation, calibratedLeftKneeRot);
            if (lKneeDelta >= legAngleThreshold) isLeftLegBent = true;
        }
        if (rightKneeBone != null)
        {
            float rKneeDelta = Quaternion.Angle(rightKneeBone.localRotation, calibratedRightKneeRot);
            if (rKneeDelta >= legAngleThreshold) isRightLegBent = true;
        }

        // --- 2. 胸（Spine）または頭の高さの沈み込み ---
        bool isBodyLowered = false;
        if (spine != null)
        {
            float spineDrop = calibratedSpineHeight - spine.position.y;
            isBodyLowered = spineDrop >= squatDropThreshold;
        }
        else if (headBone != null)
        {
            float headDrop = calibratedHeadHeight - headBone.position.y;
            isBodyLowered = headDrop >= squatDropThreshold;
        }

        // --- 3. 手の座標と肩（胸）との高低差の計算（握りこみ2点） ---
        bool hasArms = leftArmBone != null && rightArmBone != null;
        Vector3 leftHandPos = Vector3.zero;
        Vector3 rightHandPos = Vector3.zero;
        bool leftHandUp = false;
        bool rightHandUp = false;
        bool leftHandDown = false;
        bool rightHandDown = false;
        bool leftHandHorizontal = false;
        bool rightHandHorizontal = false;
        float armSpan = 0f;

        if (hasArms)
        {
            // 握りこみボーンの先端位置
            leftHandPos = leftArmBone.TransformPoint(new Vector3(-0.5f, 0, 0));
            rightHandPos = rightArmBone.TransformPoint(new Vector3(0.5f, 0, 0));

            float leftShoulderY = leftArmBone.position.y;
            float rightShoulderY = rightArmBone.position.y;

            // バンザイ（肩より上）
            leftHandUp = leftHandPos.y > (leftShoulderY + banzaiHeightThreshold);
            rightHandUp = rightHandPos.y > (rightShoulderY + banzaiHeightThreshold);

            // 腕下げ（肩より下）
            leftHandDown = leftHandPos.y < (leftShoulderY - armDownThreshold);
            rightHandDown = rightHandPos.y < (rightShoulderY - armDownThreshold);

            // 水平（肩付近の高さ）
            leftHandHorizontal = Mathf.Abs(leftHandPos.y - leftShoulderY) <= tPoseHeightTolerance;
            rightHandHorizontal = Mathf.Abs(rightHandPos.y - rightShoulderY) <= tPoseHeightTolerance;

            armSpan = Mathf.Abs(leftHandPos.x - rightHandPos.x);
        }

        // ===== ポーズ判定（優先度順） =====
        // A. しゃがみ (Squat): 胸が下がっている、または両足/両膝が曲がっている
        if (isBodyLowered || (isLeftLegBent && isRightLegBent))
        {
            detectedPose = (int)PoseType.Squat;
        }
        // B. バンザイ (Banzai): 握りこんだ両手が上がっている
        else if (leftHandUp && rightHandUp)
        {
            detectedPose = (int)PoseType.Banzai;
        }
        // C. 右腕上げ (RightArmUp): 右手が上 かつ 左手が下
        else if (rightHandUp && leftHandDown)
        {
            detectedPose = (int)PoseType.RightArmUp;
        }
        // D. 左腕上げ (LeftArmUp): 左手が上 かつ 右手が下
        else if (leftHandUp && rightHandDown)
        {
            detectedPose = (int)PoseType.LeftArmUp;
        }
        // E. Tポーズ (TPose): 両腕が水平かつ横に十分広がっている
        else if (leftHandHorizontal && rightHandHorizontal && armSpan >= tPoseWidthMinSpan)
        {
            detectedPose = (int)PoseType.TPose;
        }
        // F. 片足立ち / フラミンゴ (Flamingo): 片方の足/膝だけが曲がっている
        else if (isLeftLegBent ^ isRightLegBent)
        {
            detectedPose = (int)PoseType.Flamingo;
        }
        // G. それ以外: 棒立ち (Stand)
        else
        {
            detectedPose = (int)PoseType.Stand;
        }

        // チラつき防止（デバウンス処理）
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
        Color poseColor = Color.white;

        switch ((PoseType)currentPose)
        {
            case PoseType.Stand:
                currentPoseName = "STAND";
                poseColor = Color.white;
                break;
            case PoseType.Banzai:
                currentPoseName = "BANZAI";
                poseColor = Color.red;
                break;
            case PoseType.Squat:
                currentPoseName = "SQUAT";
                poseColor = new Color(0.2f, 0.6f, 1f); // スカイブルー
                break;
            case PoseType.TPose:
                currentPoseName = "T-POSE";
                poseColor = new Color(0.2f, 1f, 0.3f); // エメラルドグリーン
                break;
            case PoseType.RightArmUp:
                currentPoseName = "RIGHT ARM UP";
                poseColor = new Color(1f, 0.85f, 0.1f); // イエロー
                break;
            case PoseType.LeftArmUp:
                currentPoseName = "LEFT ARM UP";
                poseColor = new Color(0.1f, 0.95f, 0.95f); // シアン
                break;
            case PoseType.Flamingo:
                currentPoseName = "FLAMINGO (1-LEG)";
                poseColor = new Color(1f, 0.25f, 0.8f); // マゼンタ
                break;
            default:
                currentPoseName = $"POSE {currentPose}";
                poseColor = Color.white;
                break;
        }

        ApplyPoseColor(poseColor);
        Debug.Log($"[PoseDetector] Pose Detected: {currentPoseName} (ID: {currentPose})");
    }

    void ApplyPoseColor(int poseId)
    {
        ChangePose(poseId);
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
        // 右上に現在のポーズ名を表示
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 20;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;

        switch ((PoseType)currentPose)
        {
            case PoseType.Banzai: style.normal.textColor = Color.red; break;
            case PoseType.Squat: style.normal.textColor = new Color(0.2f, 0.6f, 1f); break;
            case PoseType.TPose: style.normal.textColor = Color.green; break;
            case PoseType.RightArmUp: style.normal.textColor = Color.yellow; break;
            case PoseType.LeftArmUp: style.normal.textColor = Color.cyan; break;
            case PoseType.Flamingo: style.normal.textColor = Color.magenta; break;
            default: style.normal.textColor = Color.white; break;
        }

        GUI.Box(new Rect(Screen.width - 280, 10, 270, 50), $"POSE: {currentPoseName}", style);

        // キャリブレーション通知メッセージ
        if (calibrationMessageTimer > 0f)
        {
            GUIStyle calibStyle = new GUIStyle(GUI.skin.box);
            calibStyle.fontSize = 22;
            calibStyle.fontStyle = FontStyle.Bold;
            calibStyle.alignment = TextAnchor.MiddleCenter;
            calibStyle.normal.textColor = Color.yellow;
            GUI.Box(new Rect(Screen.width * 0.25f, 70, Screen.width * 0.5f, 45), calibrationMessage, calibStyle);
        }
    }
}


