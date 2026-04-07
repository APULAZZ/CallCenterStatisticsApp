namespace CallCenterStatisticsApp.Models;

public class EmployeeGroup
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int GroupId { get; set; }
    public CallGroup? Group { get; set; }

    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}