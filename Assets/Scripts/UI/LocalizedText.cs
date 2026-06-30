using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class LocalizedText : MonoBehaviour
{
    public string key = "ui.text";
    [TextArea]
    public string chineseText;
    [TextArea]
    public string englishText;

    private TextMeshProUGUI text;

    void Awake()
    {
        text = GetComponent<TextMeshProUGUI>();
    }

    void OnEnable()
    {
        DuoCurtainLocalization.LanguageChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        DuoCurtainLocalization.LanguageChanged -= Refresh;
    }

    public void Refresh()
    {
        if (text == null)
            text = GetComponent<TextMeshProUGUI>();

        if (text == null)
            return;

        string resolved = DuoCurtainLocalization.Text(key, chineseText, englishText);
        text.text = resolved;
        DuoCurtainLocalization.ApplyFont(text, resolved);
    }
}
