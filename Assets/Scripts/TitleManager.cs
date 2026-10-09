using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    public void SetDifficultyEasy()
    {
        GameSettings.wallSpeed = 3f;
        GameSettings.wallSpawnInterval = 4f;
        Debug.Log("難易度: 簡単 に設定");
    }

    public void SetDifficultyNormal()
    {
        GameSettings.wallSpeed = 5f;
        GameSettings.wallSpawnInterval = 3f;
        Debug.Log("難易度: 普通 に設定");
    }

    public void SetDifficultyHard()
    {
        GameSettings.wallSpeed = 8f;
        GameSettings.wallSpawnInterval = 1.5f;
        Debug.Log("難易度: 難しい に設定");
    }

    public void StartGame()
    {
        // 実際のゲームシーン名（今のシーン名）に合わせる必要があります
        // ひとまずデフォルトの SampleScene をロードする想定
        SceneManager.LoadScene("SampleScene"); 
    }
}
