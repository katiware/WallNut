using UnityEngine;
using UnityEditor;

public class SetupOptimalCamera : EditorWindow
{
    [MenuItem("Tools/Setup Projector Camera (カメラを最適位置に配置・固定)")]
    public static void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            cam = FindObjectOfType<Camera>();
        }

        if (cam == null)
        {
            // カメラが存在しない場合は新規作成
            GameObject camObj = new GameObject("Main Camera");
            cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
            camObj.AddComponent<AudioListener>();
            Undo.RegisterCreatedObjectUndo(camObj, "Create Main Camera");
        }

        Undo.RecordObject(cam.transform, "Setup Projector Camera");
        Undo.RecordObject(cam, "Setup Projector Camera FOV");

        // プロジェクター三人称視点の黄金比アングル
        Vector3 optimalPos = new Vector3(0f, 1.7f, -4.2f);
        Vector3 optimalRot = new Vector3(6f, 0f, 0f);
        float optimalFOV = 60f;

        cam.transform.position = optimalPos;
        cam.transform.rotation = Quaternion.Euler(optimalRot);
        cam.fieldOfView = optimalFOV;

        // CameraControllerの自動アタッチ
        CameraController controller = cam.GetComponent<CameraController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<CameraController>(cam.gameObject);
        }

        PlayerDummy dummy = FindObjectOfType<PlayerDummy>();
        if (dummy != null)
        {
            controller.targetPlayer = dummy.transform;
        }

        controller.defaultPosition = optimalPos;
        controller.defaultRotation = optimalRot;
        controller.defaultFOV = optimalFOV;
        controller.cameraHeight = optimalPos.y;
        controller.cameraDistance = optimalPos.z;
        controller.cameraTilt = optimalRot.x;
        controller.cameraFOV = optimalFOV;
        controller.isLocked = true;

        EditorUtility.SetDirty(cam.gameObject);
        Selection.activeGameObject = cam.gameObject;

        Debug.Log($"🎥 [CameraSetup] Main Camera をプロジェクター最適視点にセットしました！ Position: {optimalPos}, Rotation: {optimalRot}, FOV: {optimalFOV}");
        
        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("カメラ設定完了", 
                "Main Camera をプロジェクター三人称視点の推奨位置にセットし、CameraController を設定しました！\n\n・位置: (0, 1.7, -4.2)\n・角度: 6度見下ろし\n・FOV: 60度\n\nゲーム実行中も [F1] キーでいつでも微調整・固定・保存が可能です。", 
                "OK");
        }
    }
}
