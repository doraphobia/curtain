using System;

public static class StageIds
{
    public const string DayTop = "DayTop";
    public const string DayBottom = "DayBottom";
    public const string BeforeNight = "BeforeNight";
    public const string Night = "Night";

    public static bool Matches(string currentStageId, string expectedStageId)
    {
        return string.Equals(currentStageId, expectedStageId, StringComparison.Ordinal);
    }

    public static bool IsNight(string stageId)
    {
        return Matches(stageId, Night);
    }
}
