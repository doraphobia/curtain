using UnityEngine;

[DisallowMultipleComponent]
public class GameRunStats : MonoBehaviour
{
    private static GameRunStats instance;

    public static GameRunStats Instance
    {
        get
        {
            if (instance != null)
                return instance;

            GameObject go = new GameObject("GameRunStats");
            instance = go.AddComponent<GameRunStats>();
            DontDestroyOnLoad(go);
            return instance;
        }
    }

    public int DaysSurvived { get; private set; }
    public int CorrectEventChoices { get; private set; }
    public int WrongEventChoices { get; private set; }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void ResetRun()
    {
        DaysSurvived = 0;
        CorrectEventChoices = 0;
        WrongEventChoices = 0;
    }

    public void RecordCompletedDay()
    {
        DaysSurvived++;
    }

    public void RecordEventChoice(bool wasCorrect)
    {
        if (wasCorrect)
            CorrectEventChoices++;
        else
            WrongEventChoices++;
    }
}
