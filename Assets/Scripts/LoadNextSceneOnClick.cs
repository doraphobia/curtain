using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class LoadNextSceneOnClick : MonoBehaviour
{
    public void LoadNextScene()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        int nextBuildIndex = currentScene.buildIndex + 1;

        if (nextBuildIndex >= SceneManager.sceneCountInBuildSettings)
            return;

        SceneManager.LoadScene(nextBuildIndex);
    }
}
