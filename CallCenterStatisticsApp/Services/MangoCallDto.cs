namespace CallCenterStatisticsApp.Services;

public class MangoCallDto
{
    public string? CallId { get; set; }
    public DateTime CallDateTime { get; set; }

    public string? Direction { get; set; }

    public string? EmployeeMangoId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeExtension { get; set; }

    public string? GroupMangoId { get; set; }
    public string? GroupName { get; set; }

    public string? TopicMangoId { get; set; }

    public string? RecordingId { get; set; }

    public string? PhoneNumber { get; set; }

    public string? StatusCode { get; set; }
    public string? StatusText { get; set; }

    public int? DurationSeconds { get; set; }
    public int? TalkDurationSeconds { get; set; }
    public int? WaitDurationSeconds { get; set; }

    public int? CallEndReason { get; set; }

    public string RawJson { get; set; } = string.Empty;
}