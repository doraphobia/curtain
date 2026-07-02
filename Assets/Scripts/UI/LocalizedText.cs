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

    #if UNITY_EDITOR
    private static readonly System.Collections.Generic.HashSet<int> MissingTranslationWarnings =
        new System.Collections.Generic.HashSet<int>();
    #endif

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

        #if UNITY_EDITOR
        WarnAboutMissingTranslationsOnce();
        #endif

        string resolved = DuoCurtainLocalization.Text(key, chineseText, englishText);
        text.text = resolved;
        DuoCurtainLocalization.ApplyFont(text, resolved);
    }

    #if UNITY_EDITOR
    private void WarnAboutMissingTranslationsOnce()
    {
        // Keep this as a gentle editor-only nudge so English mode doesn't accidentally ship Chinese strings (and vice versa).
        // Avoid spamming: warn once per component instance.
        int id = GetInstanceID();
        if (MissingTranslationWarnings.Contains(id))
            return;

        bool missingEnglish = !string.IsNullOrWhiteSpace(chineseText) && string.IsNullOrWhiteSpace(englishText);
        bool missingChinese = !string.IsNullOrWhiteSpace(englishText) && string.IsNullOrWhiteSpace(chineseText);

        if (!missingEnglish && !missingChinese)
            return;

        // Only warn when the missing side is the currently active language.
        if (DuoCurtainLocalization.CurrentLanguage == DuoCurtainLanguage.English && missingEnglish)
        {
            MissingTranslationWarnings.Add(id);
            Debug.LogWarning($"[LocalizedText] Missing English text for key='{key}' on '{name}'.", this);
        }
        else if (DuoCurtainLocalization.CurrentLanguage == DuoCurtainLanguage.Chinese && missingChinese)
        {
            MissingTranslationWarnings.Add(id);
            Debug.LogWarning($"[LocalizedText] Missing Chinese text for key='{key}' on '{name}'.", this);
        }
    }
    #endif
}
