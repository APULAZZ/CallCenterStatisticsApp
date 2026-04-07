namespace CallCenterStatisticsApp.Models;

public class CallTopic
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? MangoTopicId { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<CallRecord> CallRecords { get; set; } = new List<CallRecord>();
}