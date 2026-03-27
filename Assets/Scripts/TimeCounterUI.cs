using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

public class TimeCounterUI : MonoBehaviour
{
    [Header("UI (assign one)")]
    public TextMeshProUGUI tmpText; // 推荐
    public Text uiText;             // 旧版Text
    public Slider slider;

    [Header("Settings")]
    public bool countUp = true;     // true=向上数，false=倒计时
    public float startSeconds = 0f; // 向上：起始值；倒计时：初始剩余时间
    public bool showMilliseconds = false;
    public float maxValue = 100f;

    private float current;

    public float CurrentValue => current;
    public int CurrentWholeValue => Mathf.FloorToInt(current);
    public event Action<float> ValueChanged;

    void Start()
    {
        current = Mathf.Clamp(startSeconds, 0f, maxValue);
        UpdateLabel();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (countUp)
            current = Mathf.Min(maxValue, current + dt);

        UpdateLabel();
    }

    void UpdateLabel()
    {
        string s;

        if (showMilliseconds)
        {
            // 例如 12.34s
            s = $"{current:0.00}";
        }
        else
        {
            // 只显示整数秒
            int sec = Mathf.FloorToInt(current);
            s = $"{sec}";
        }

        if (tmpText != null) tmpText.text = s;
        if (uiText != null) uiText.text = s;
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = maxValue;
            slider.value = Mathf.Clamp(current, 0f, maxValue);
        }

        ValueChanged?.Invoke(current);
    }

    // 可选：外部重置
    public void ResetCounter(float seconds)
    {
        current = Mathf.Clamp(seconds, 0f, maxValue);
        UpdateLabel();
    }

    public bool CanAfford(int amount)
    {
        return amount <= CurrentWholeValue;
    }

    public bool TrySpend(int amount)
    {
        if (amount < 0)
            return false;

        if (!CanAfford(amount))
            return false;

        current -= amount;
        if (current < 0f)
            current = 0f;

        UpdateLabel();
        return true;
    }

    public void AddValue(float amount)
    {
        current = Mathf.Clamp(current + amount, 0f, maxValue);
        UpdateLabel();
    }
}
