using UnityEngine;

public class PlayerDummy : MonoBehaviour
{
    [Tooltip("現在のポーズ状態 (0: 棒立ち, 1: バンザイ, 2: しゃがみ)")]
    public int currentPose = 0; 
    public Renderer playerRenderer; // 色を変えて現在のポーズをわかりやすくする

    void Start()
    {
        if (playerRenderer == null) playerRenderer = GetComponent<Renderer>();
        ChangePose(0, Color.white);
    }

    void Update()
    {
        // キーボードの1, 2, 3でポーズを切り替える（トラッカーがない間の一時的な処理）
        if (Input.GetKeyDown(KeyCode.Alpha1)) ChangePose(0, Color.white);
        if (Input.GetKeyDown(KeyCode.Alpha2)) ChangePose(1, Color.red);
        if (Input.GetKeyDown(KeyCode.Alpha3)) ChangePose(2, Color.blue);
    }

    void ChangePose(int poseId, Color color)
    {
        currentPose = poseId;
        if (playerRenderer != null)
        {
            playerRenderer.material.color = color;
        }
        Debug.Log($"[Player] ポーズ変更: {poseId}");
    }
}
