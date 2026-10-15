using UnityEngine;
using UnityEditor;

public class DummyCharacterBuilder : EditorWindow
{
    [MenuItem("Tools/Build Dummy Character")]
    public static void BuildDummy()
    {
        // 既存のDummyCharacterがあれば重複を防ぐため削除
        GameObject existing = GameObject.Find("DummyCharacter");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        // ルートオブジェクト
        GameObject root = new GameObject("DummyCharacter");

        // --- 体の各パーツを作成 ---
        // 1. 胴体 (Spine) - 地面から少し浮かせた位置に配置
        GameObject spine = CreateBone("Spine", root.transform, new Vector3(0, 1.0f, 0), PrimitiveType.Cube, new Vector3(0, 0, 0), new Vector3(0.4f, 0.6f, 0.2f));

        // 2. 頭 (Head) - 胴体の上
        GameObject head = CreateBone("HeadBone", spine.transform, new Vector3(0, 0.35f, 0), PrimitiveType.Sphere, new Vector3(0, 0.2f, 0), new Vector3(0.3f, 0.3f, 0.3f));

        // 頭の正面に「鼻（赤い突起）」を付けて、向いている方向を一目で分かるようにする
        GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Nose_Mesh";
        nose.transform.SetParent(head.transform);
        nose.transform.localPosition = new Vector3(0, 0.2f, 0.18f); // 顔の前方(+Z)に突き出す
        nose.transform.localScale = new Vector3(0.08f, 0.08f, 0.15f);
        DestroyImmediate(nose.GetComponent<Collider>());
        // 鼻を赤くして目立たせる
        Renderer noseRenderer = nose.GetComponent<Renderer>();
        if (noseRenderer != null)
        {
            noseRenderer.material.color = Color.red;
        }

        // 3. 腕 (Arms) - Tポーズになるように横に伸ばす
        GameObject lArm = CreateBone("LeftArmBone", spine.transform, new Vector3(-0.25f, 0.2f, 0), PrimitiveType.Cube, new Vector3(-0.3f, 0, 0), new Vector3(0.6f, 0.1f, 0.1f));
        GameObject rArm = CreateBone("RightArmBone", spine.transform, new Vector3(0.25f, 0.2f, 0), PrimitiveType.Cube, new Vector3(0.3f, 0, 0), new Vector3(0.6f, 0.1f, 0.1f));

        // 4. 足の付け根 (Hips)
        GameObject lHip = CreateBone("LeftHipBone", root.transform, new Vector3(-0.15f, 0.7f, 0), PrimitiveType.Cube, new Vector3(0, -0.35f, 0), new Vector3(0.15f, 0.7f, 0.15f));
        GameObject rHip = CreateBone("RightHipBone", root.transform, new Vector3(0.15f, 0.7f, 0), PrimitiveType.Cube, new Vector3(0, -0.35f, 0), new Vector3(0.15f, 0.7f, 0.15f));

        // 5. 足首〜つま先 (Feet)
        GameObject lFoot = CreateBone("LeftFootBone", lHip.transform, new Vector3(0, -0.7f, 0), PrimitiveType.Cube, new Vector3(0, -0.05f, 0.1f), new Vector3(0.15f, 0.1f, 0.25f));
        GameObject rFoot = CreateBone("RightFootBone", rHip.transform, new Vector3(0, -0.7f, 0), PrimitiveType.Cube, new Vector3(0, -0.05f, 0.1f), new Vector3(0.15f, 0.1f, 0.25f));

        // --- Receiverへ自動割り当て ---
        SlimeVRTestReceiver receiver = FindObjectOfType<SlimeVRTestReceiver>();
        if (receiver != null)
        {
            Undo.RecordObject(receiver, "Assign Dummy Bones");
            receiver.headBone = head.transform;
            receiver.leftFootBone = lFoot.transform;
            receiver.rightFootBone = rFoot.transform;
            receiver.leftArmBone = lArm.transform;
            receiver.rightArmBone = rArm.transform;
            Debug.Log("🤖 ダミーキャラクターを生成し、頭・足・腕のボーンをSlimeVRTestReceiverに自動割り当てしました！");

            // MockSenderがシーンに存在しなければ、Receiverと同じオブジェクトに自動追加
            SlimeVRMockSender sender = FindObjectOfType<SlimeVRMockSender>();
            if (sender == null)
            {
                Undo.AddComponent<SlimeVRMockSender>(receiver.gameObject);
                Debug.Log("📡 シーン内にSlimeVRMockSenderが見つからなかったため、自動追加しました！");
            }
        }
        else
        {
            Debug.Log("🤖 ダミーキャラクターを生成しました！（※シーン内にSlimeVRTestReceiverが見つからなかったため、手動で割り当ててください）");
        }

        // 生成したオブジェクトを選択状態にする
        Selection.activeGameObject = root;
        // Undoシステムに登録
        Undo.RegisterCreatedObjectUndo(root, "Create Dummy Character");
    }

    // 関節（ピボット）となる空オブジェクトと、見た目用のメッシュをセットで作るヘルパー関数
    private static GameObject CreateBone(string name, Transform parent, Vector3 localPos, PrimitiveType meshType, Vector3 meshOffset, Vector3 meshScale)
    {
        // 1. 関節（回転の中心軸）となる空オブジェクト
        GameObject bone = new GameObject(name);
        bone.transform.SetParent(parent);
        bone.transform.localPosition = localPos;

        // 2. 見た目（メッシュ）
        GameObject meshObj = GameObject.CreatePrimitive(meshType);
        meshObj.name = name + "_Mesh";
        meshObj.transform.SetParent(bone.transform);
        meshObj.transform.localPosition = meshOffset;
        meshObj.transform.localScale = meshScale;

        // 今回は物理演算や当たり判定のテストではないので、コライダーを削除
        DestroyImmediate(meshObj.GetComponent<Collider>());

        return bone;
    }
}
