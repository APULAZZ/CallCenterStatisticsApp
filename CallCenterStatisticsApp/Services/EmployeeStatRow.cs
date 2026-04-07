namespace CallCenterStatisticsApp.Services;

public class EmployeeStatRow
{
    public int? EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;

    public int IncomingCount { get; set; }
    public int IncomingMissedCount { get; set; }
    public int OutgoingCount { get; set; }
    public int OutgoingNoAnswerCount { get; set; }
}