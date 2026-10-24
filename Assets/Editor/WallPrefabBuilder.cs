using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class WallPrefabBuilder : EditorWindow
{
    private struct PoseDef
    {
        public int id;
        public string name;
        public Color color;

        public PoseDef(int id, string name, Color color)
        {
            this.id = id;
            this.name = name;
            this.color = color;
        }
    }

    [MenuItem("Tools/Generate All Wall Prefabs (穴あき壁プレハブ一括生成)")]
    public static void GenerateAllPrefabs()
    {
        // 1. 必要なフォルダの作成
        EnsureDirectoryExists("Assets/Materials");
        EnsureDirectoryExists("Assets/Prefabs");

        // 2. 全7ポーズの定義
        PoseDef[] poses = new PoseDef[]
        {
            new PoseDef(0, "Stand", Color.white),
            new PoseDef(1, "Banzai", new Color(0.95f, 0.2f, 0.2f)),
            new PoseDef(2, "Squat", new Color(0.2f, 0.6f, 1f)),
            new PoseDef(3, "TPose", new Color(0.2f, 0.95f, 0.3f)),
            new PoseDef(4, "RightArmUp", new Color(1f, 0.85f, 0.1f)),
            new PoseDef(5, "LeftArmUp", new Color(0.1f, 0.95f, 0.95f)),
            new PoseDef(6, "Flamingo", new Color(1f, 0.25f, 0.8f))
        };

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") 
                     ?? Shader.Find("Standard") 
                     ?? Shader.Find("Mobile/Diffuse");

        List<GameObject> generatedPrefabs = new List<GameObject>();

        int width = 5;
        int height = 5;
        float blockSize = 0.46f; // 人間の身長1.75m・肩幅・腕スパンにピッタリ一致
        float blockGapSize = 0.445f; // ブロックの境目がわかるようにわずかに隙間をあける（もじもじくんブロック風）
        float thickness = 0.4f;

        float startX = -((width - 1) * blockSize) / 2f;
        float startY = blockSize / 2f;

        // 3. 各ポーズの壁オブジェクトを作成してプレハブ保存
        for (int i = 0; i < poses.Length; i++)
        {
            PoseDef p = poses[i];

            // マテリアルの作成・保存
            string matPath = $"Assets/Materials/Mat_Wall_{p.id}_{p.name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.color = p.color;
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.color = p.color;
                EditorUtility.SetDirty(mat);
            }

            // ルートGameObjectの作成
            string objName = $"Wall_{p.id}_{p.name}";
            GameObject root = new GameObject(objName);

            // コンポーネントのアタッチ
            WallMover mover = root.AddComponent<WallMover>();
            mover.requiredPose = p.id;
            mover.speed = 5f;

            WallBuilder builder = root.AddComponent<WallBuilder>();
            builder.width = width;
            builder.height = height;
            builder.blockSize = blockSize;

            // 5x5ブロックを組み立て（穴のマスには配置しない）
            int blockCount = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    // 穴の部分（ポーズの形）はスキップ！
                    if (WallBuilder.IsHole(p.id, x, y))
                    {
                        continue;
                    }

                    GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    block.name = $"Block_{x}_{y}";
                    block.transform.SetParent(root.transform);
                    block.transform.localPosition = new Vector3(startX + x * blockSize, startY + y * blockSize, 0f);
                    block.transform.localScale = new Vector3(blockGapSize, blockGapSize, thickness);

                    // マテリアルの適用
                    Renderer r = block.GetComponent<Renderer>();
                    if (r != null)
                    {
                        r.sharedMaterial = mat;
                    }

                    blockCount++;
                }
            }

            // プレハブとして保存
            string prefabPath = $"Assets/Prefabs/{objName}.prefab";
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            generatedPrefabs.Add(savedPrefab);

            // シーン内の一時オブジェクトを破棄
            DestroyImmediate(root);

            Debug.Log($"🧱 穴あき壁プレハブを生成しました: {prefabPath} (ブロック数: {blockCount})");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 4. GameManagerへの自動割り当て
        GameManager gm = FindObjectOfType<GameManager>();
        if (gm != null)
        {
            Undo.RecordObject(gm, "Assign Wall Prefabs");
            gm.wallPrefabs = generatedPrefabs.ToArray();
            EditorUtility.SetDirty(gm);
            Debug.Log($"🎯 GameManagerに全7種の壁プレハブ（{generatedPrefabs.Count}個）を自動登録しました！");
        }
        else
        {
            Debug.LogWarning("⚠️ シーン内にGameManagerが見つかりませんでした。プレハブは Assets/Prefabs に保存されています。");
        }

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("壁プレハブ一括生成 完了", 
                $"全{poses.Length}種類の「人の形の穴があいた壁プレハブ」を Assets/Prefabs/ に生成しました！\nGameManagerへの登録も完了しています。", 
                "OK");
        }
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string folderName = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
