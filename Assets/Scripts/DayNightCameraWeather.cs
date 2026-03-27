using UnityEngine;

public class DayNightCameraWeather : MonoBehaviour
{
    [Header("Target")]
    public Camera targetCamera;

    [Header("Colors")]
    public Color dayColor = Color.white;
    public Color nightColor = Color.black;

    [Header("Timing (seconds)")]
    [Tooltip("每次白天/黑夜状态持续多久（包含渐变时间）")]
    public float cycleDuration = 10f;

    [Tooltip("在切换前多少秒开始均匀渐变")]
    public float transitionDuration = 1f;

    private float timer = 0f;
    private bool isDay = true;

    void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        // 初始设为白天
        ApplyColorImmediate(dayColor);
        timer = 0f;
        isDay = true;
    }

    void Update()
    {
        if (targetCamera == null) return;

        // 防呆：避免除以0或负数
        float cd = Mathf.Max(0.01f, cycleDuration);
        float td = Mathf.Clamp(transitionDuration, 0f, cd);

        timer += Time.deltaTime;

        // 循环计时
        if (timer >= cd)
        {
            timer -= cd;
            isDay = !isDay; // 切换状态
        }

        // 当前状态下：我们“将要切换到”的目标颜色
        // 例如 isDay=true 表示当前阶段是白天，快结束时会切去夜晚
        Color holdColor = isDay ? dayColor : nightColor;
        Color nextColor = isDay ? nightColor : dayColor;

        // 前 (cd - td) 秒保持不变；最后 td 秒线性渐变到 nextColor
        float transitionStart = cd - td;

        if (td <= 0f || timer < transitionStart)
        {
            // 不在转换窗口：直接保持当前颜色
            ApplyColorImmediate(holdColor);
        }
        else
        {
            // 在最后 td 秒：均匀变色到目标
            float t = Mathf.InverseLerp(transitionStart, cd, timer); // 0->1
            targetCamera.backgroundColor = Color.Lerp(holdColor, nextColor, t);
        }
    }

    private void ApplyColorImmediate(Color c)
    {
        // 避免每帧重复写入导致一些管线/后处理出现抖动（可选优化）
        if (targetCamera.backgroundColor != c)
            targetCamera.backgroundColor = c;
    }
}