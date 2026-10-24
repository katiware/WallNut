using UnityEngine;

public class WallBuilder : MonoBehaviour
{
    [Tooltip("壁を構成する1つ1つのブロック（Cubeプレハブ）")]
    public GameObject blockPrefab;
    
    [Tooltip("壁の横ブロック数")]
    public int width = 5;
    [Tooltip("壁の縦ブロック数")]
    public int height = 5;
    public float blockSize = 1f;

    void Start()
    {
        BuildWall();
    }

    void BuildWall()
    {
        if (blockPrefab == null)
        {
            // デフォルトのCubeを作成して代用する
            blockPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blockPrefab.transform.localScale = Vector3.one * blockSize;
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
    bool IsHole(int poseId, int x, int y)
    {
        // 0: 棒立ち (真ん中一列だけ空ける)
        if (poseId == 0)
        {
            if (x == 2) return true;
        }
        // 1: バンザイ (Y字型に空ける)
        else if (poseId == 1)
        {
            if (x == 2 && y <= 2) return true; // 足と胴体
            if (x == 2 && y == 3) return true; // 頭
            if ((x == 1 || x == 3) && (y == 3 || y == 4)) return true; // 両手
        }
        // 2: しゃがみ (足元の中央だけ広く空ける)
        else if (poseId == 2)
        {
            if (x >= 1 && x <= 3 && y <= 2) return true;
        }
        // 3: Tポーズ (十字型：中央一列 + 肩の高さに横一列)
        else if (poseId == 3)
        {
            if (x == 2) return true; // 体と頭
            if (y == 3) return true; // 横に伸ばした両腕
        }
        // 4: 右腕上げ (右側だけ上部に腕の穴)
        else if (poseId == 4)
        {
            if (x == 2) return true; // 体と頭
            if (x == 3 && (y == 3 || y == 4)) return true; // 右腕
        }
        // 5: 左腕上げ (左側だけ上部に腕の穴)
        else if (poseId == 5)
        {
            if (x == 2) return true; // 体と頭
            if (x == 1 && (y == 3 || y == 4)) return true; // 左腕
        }
        // 6: 片足立ち / フラミンゴ (片足を曲げて横に突き出す)
        else if (poseId == 6)
        {
            if (x == 2) return true; // 体と軸足
            if (x == 3 && (y == 1 || y == 2)) return true; // 曲げた片足
        }

        return false;
    }
}
