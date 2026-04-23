using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class ResultSummaryUI : MonoBehaviour
{
    [Header("References")]
    public TMP_Text daysText;
    public TMP_Text correctChoicesText;
    public TMP_Text wrongChoicesText;

    [Header("Format")]
    public string daysFormat = "Survived Days: {0}";
    public string correctFormat = "Correct Choices: {0}";
    public string wrongFormat = "Wrong Choices: {0}";

    void Start()
    {
        Refresh();
    }

    public void Refresh()
    {
        GameRunStats stats = GameRunStats.Instance;

        if (daysText != null)
            daysText.text = string.Format(daysFormat, stats.DaysSurvived);

        if (correctChoicesText != null)
            correctChoicesText.text = string.Format(correctFormat, stats.CorrectEventChoices);

        if (wrongChoicesText != null)
            wrongChoicesText.text = string.Format(wrongFormat, stats.WrongEventChoices);
    }
}
