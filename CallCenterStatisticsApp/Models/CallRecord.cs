namespace CallCenterStatisticsApp.Models;

public class CallRecord
{
    public int Id { get; set; }

    public string MangoCallId { get; set; } = string.Empty;
    public DateTime CallDateTime { get; set; }

    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int? GroupId { get; set; }
    public CallGroup? Group { get; set; }

    public int? TopicId { get; set; }
    public CallTopic? Topic { get; set; }

    public string? RecordingId { get; set; }

    public string? ExternalPhoneNumber { get; set; }

    public string Direction { get; set; } = string.Empty;
    public string? StatusCode { get; set; }
    public string? StatusText { get; set; }

    public int? DurationSeconds { get; set; }
    public int? TalkDurationSeconds { get; set; }
    public int? WaitDurationSeconds { get; set; }

    public bool IsIncoming { get; set; }
    public bool IsOutgoing { get; set; }
    public bool IsAnswered { get; set; }
    public bool IsMissedIncoming { get; set; }
    public bool IsOutgoingNoAnswer { get; set; }

    public string? RawJson { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.Now;
}