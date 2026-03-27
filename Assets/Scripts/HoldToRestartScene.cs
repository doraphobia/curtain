using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class HoldToRestartScene : MonoBehaviour
{
    [Header("Input")]
    public KeyCode restartKey = KeyCode.R;
    [Min(0.1f)]
    public float holdDuration = 1f;

    private float holdTimer;

    void Update()
    {
        if (Input.GetKey(restartKey))
        {
            holdTimer += Time.deltaTime;

            if (holdTimer >= holdDuration)
            {
                RestartCurrentScene();
            }
        }
        else
        {
            holdTimer = 0f;
        }
    }

    public void RestartCurrentScene()
    {
        holdTimer = 0f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
