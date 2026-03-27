using UnityEngine;

public class PauseManager : MonoBehaviour
{
    [Header("Key")]
    public KeyCode toggleKey = KeyCode.Space;

    [Header("Options")]
    [Tooltip("暂停时是否也暂停音频（全局）")]
    public bool pauseAudio = true;

    private bool paused = false;
    private float prevTimeScale = 1f;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        if (!paused)
        {
            // Pause
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            paused = true;

            if (pauseAudio) AudioListener.pause = true;
        }
        else
        {
            // Resume
            Time.timeScale = prevTimeScale <= 0f ? 1f : prevTimeScale;
            paused = false;

            if (pauseAudio) AudioListener.pause = false;
        }
    }

    public bool IsPaused() => paused;

    void OnDisable()
    {
        // 防止物体被禁用/切场景后卡死在暂停
        if (paused)
        {
            Time.timeScale = 1f;
            paused = false;
            if (pauseAudio) AudioListener.pause = false;
        }
    }
}