using UnityEngine;

public class WallBuilder : MonoBehaviour
{
    [Tooltip("壁を構成する1つ1つのブロック（Cubeプレハブ）")]
    public GameObject blockPrefab;
    
    [Tooltip("壁の横ブロック数")]
    public int width = 5;
    [Tooltip("壁の縦ブロック数")]
    public int height = 5;
    [Tooltip("ブロック1個のサイズ（0.46mで人間の身長1.75m・肩幅・腕スパンにピッタリ一致）")]
    public float blockSize = 0.46f;
    [Tooltip("壁の厚み")]
    public float wallThickness = 0.4f;

    void Start()
    {
        // すでにエディタで構築されたブロック（子オブジェクト）が存在する場合は二重生成しない
        if (transform.childCount > 0) return;
        BuildWall();
    }

    void BuildWall()
    {
        if (blockPrefab == null)
        {
            // デフォルトのCubeを作成して代用する
            blockPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blockPrefab.transform.localScale = new Vector3(blockSize * 0.97f, blockSize * 0.97f, wallThickness);
            // 実行時に生成したプレハブ代わりのオブジェクトは一旦非表示にする
            blockPrefab.SetActive(false);
        }

        WallMover mover = GetComponent<WallMover>();
        int poseId = mover != null ? mover.requiredPose : 0;

        // 中心が x=0 になるように開始位置を計算
        float startX = -((width - 1) * blockSize) / 2f;
        // Yは0（足元）から上に積む
        float startY = blockSize / 2f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // 現在の座標が「穴（もじもじくんのポーズ）」かどうか判定
                if (IsHole(poseId, x, y))
                {
                    continue; // 穴の部分にはブロックを置かない
                }

                Vector3 pos = new Vector3(startX + x * blockSize, startY + y * blockSize, 0f);
                GameObject block = Instantiate(blockPrefab, transform);
                block.transform.localPosition = pos;
                block.SetActive(true); // 代用キューブを使った場合のためにアクティブ化
            }
        }

        // プレハブ代わりのオブジェクトを削除
        if (blockPrefab.scene.IsValid()) 
        {
            Destroy(blockPrefab);
        }

        // もともとの仮の1枚板（Cube）のメッシュを非表示にする
        Renderer r = GetComponent<Renderer>();
        if (r != null) r.enabled = false;
    }

    // 5x5 (x:0~4, y:0~4) のグリッドで穴の形を定義する
    // y=0: 足元・すね (0.00〜0.46m)
    // y=1: ひざ・太もも (0.46〜0.92m)
    // y=2: 胴体・肩・Tポーズ腕 (0.92〜1.38m - 肩高1.2mが中央)
    // y=3: 首・頭 (1.38〜1.84m - 頭1.55mが中央)
    // y=4: バンザイ手先 (1.84〜2.30m)
    // x=2: 体の中心 (幅0.46m)
    public static bool IsHole(int poseId, int x, int y)
    {
        // 0: 棒立ち (中央縦一列 y:0〜3 足元から頭まで)
        if (poseId == 0)
        {
            if (x == 2 && y <= 3) return true;
        }
        // 1: バンザイ (体y:0〜3 + 斜め上に伸ばした両手y:3〜4)
        else if (poseId == 1)
        {
            if (x == 2 && y <= 3) return true; // 足と胴体と頭
            if ((x == 1 || x == 3) && (y == 3 || y == 4)) return true; // 両手
        }
        // 2: しゃがみ (足元の中央広域穴 y:0〜2)
        else if (poseId == 2)
        {
            if (x >= 1 && x <= 3 && y <= 2) return true;
        }
        // 3: Tポーズ (体y:0〜3 + 肩の高さy:2に横一列全通の両腕)
        else if (poseId == 3)
        {
            if (x == 2 && y <= 3) return true; // 体と頭
            if (y == 2) return true; // 肩の高さに横一列全通の両腕
        }
        // 4: 右腕上げ (体y:0〜3 + 右手y:3〜4)
        else if (poseId == 4)
        {
            if (x == 2 && y <= 3) return true; // 体と頭
            if (x == 3 && (y == 3 || y == 4)) return true; // 上げた右腕
        }
        // 5: 左腕上げ (体y:0〜3 + 左手y:3〜4)
        else if (poseId == 5)
        {
            if (x == 2 && y <= 3) return true; // 体と頭
            if (x == 1 && (y == 3 || y == 4)) return true; // 上げた左腕
        }
        // 6: 片足立ち / フラミンゴ (体y:0〜3 + ひざの高さy:1で横に曲げた足)
        else if (poseId == 6)
        {
            if (x == 2 && y <= 3) return true; // 体と軸足
            if (x == 3 && y == 1) return true; // 曲げた片足のひざ
        }

        return false;
    }
}
