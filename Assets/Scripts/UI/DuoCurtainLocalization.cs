using System;
using TMPro;
using UnityEngine;

public enum DuoCurtainLanguage
{
    Chinese,
    English
}

public static class DuoCurtainLocalization
{
    private const string LanguagePrefsKey = "DuoCurtain.Language";

    public static event Action LanguageChanged;

    private static bool initialized;
    private static DuoCurtainLanguage currentLanguage = DuoCurtainLanguage.Chinese;

    public static DuoCurtainLanguage CurrentLanguage
    {
        get
        {
            EnsureInitialized();
            return currentLanguage;
        }
        set
        {
            EnsureInitialized();
            if (currentLanguage == value)
                return;

            currentLanguage = value;
            PlayerPrefs.SetInt(LanguagePrefsKey, (int)currentLanguage);
            PlayerPrefs.Save();
            LanguageChanged?.Invoke();
        }
    }

    public static bool IsChinese => CurrentLanguage == DuoCurtainLanguage.Chinese;

    public static void ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage == DuoCurtainLanguage.Chinese
            ? DuoCurtainLanguage.English
            : DuoCurtainLanguage.Chinese;
    }

    public static string Text(string key, string chinese, string english)
    {
        return CurrentLanguage == DuoCurtainLanguage.Chinese ? chinese : english;
    }

    public static string Format(string key, string chineseFormat, string englishFormat, params object[] args)
    {
        string format = Text(key, chineseFormat, englishFormat);
        return string.Format(format, args);
    }

    public static void ApplyFont(TextMeshProUGUI text, string contentToPrime = null)
    {
        if (text == null)
            return;

        TMP_FontAsset font = CjkUiFontUtility.Resolve(null, CjkUiFontUtility.DefaultResourcesFontPath, contentToPrime ?? text.text);
        if (font != null)
            text.font = font;
    }

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        initialized = true;
        currentLanguage = (DuoCurtainLanguage)Mathf.Clamp(
            PlayerPrefs.GetInt(LanguagePrefsKey, (int)DuoCurtainLanguage.Chinese),
            (int)DuoCurtainLanguage.Chinese,
            (int)DuoCurtainLanguage.English);
    }
}
