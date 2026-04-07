namespace CallCenterStatisticsApp.Models;

public class CallGroup
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? MangoGroupId { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<EmployeeGroup> EmployeeGroups { get; set; } = new List<EmployeeGroup>();
    public ICollection<CallRecord> CallRecords { get; set; } = new List<CallRecord>();
}