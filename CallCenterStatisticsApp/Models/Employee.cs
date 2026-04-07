namespace CallCenterStatisticsApp.Models;

public class Employee
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string? Extension { get; set; }

    public string? MangoUserId { get; set; }
    public string? MangoUserKey { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<EmployeeGroup> EmployeeGroups { get; set; } = new List<EmployeeGroup>();
    public ICollection<CallRecord> CallRecords { get; set; } = new List<CallRecord>();
}