namespace CallCenterStatisticsApp.Models;

public class MangoSyncLog
{
    public int Id { get; set; }

    public string SyncType { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }

    public int ImportedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }

    public bool IsSuccess { get; set; }
    public string? ErrorText { get; set; }
}