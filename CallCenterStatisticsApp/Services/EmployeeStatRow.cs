namespace CallCenterStatisticsApp.Services;

public class EmployeeStatRow
{
    public int? EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public bool IsTotal { get; set; }

    public int IncomingAcceptedCount { get; set; }
    public int IncomingAcceptedWithoutTransfersCount { get; set; }
    public int OutgoingCount { get; set; }
    public int OutgoingNoAnswerCount { get; set; }
    public int InternalOutgoingCount { get; set; }
    public int TransfersCount { get; set; }
    public int MissedCount { get; set; }
    public int TopicsTotalCount { get; set; }
    public int TopicsWithoutTransfersCount { get; set; }
    public int TopicsInTransfersCount { get; set; }
}

public sealed class EmployeeStatisticsFilter
{
    public IReadOnlyCollection<int>? EmployeeIds { get; init; }
    public bool WithoutEmployees { get; init; }
    public IReadOnlyCollection<int>? GroupIds { get; init; }
    public bool WithoutGroups { get; init; }
    public bool LimitEmployeesToGroups { get; init; }
    public IReadOnlyCollection<int>? TopicIds { get; init; }
    public bool WithoutTopics { get; init; }
    public bool IgnoreTopics { get; init; }
}
