namespace CallCenterStatisticsApp.Services;

public class GroupStatRow
{
    public int? GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;

    public int IncomingCount { get; set; }
    public int IncomingMissedCount { get; set; }
    public int OutgoingCount { get; set; }
    public int OutgoingNoAnswerCount { get; set; }
}