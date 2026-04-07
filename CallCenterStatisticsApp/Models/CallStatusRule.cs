namespace CallCenterStatisticsApp.Models;

public class CallStatusRule
{
    public int Id { get; set; }

    public string StatusCode { get; set; } = string.Empty;
    public string? StatusText { get; set; }

    public bool CountAsAnswered { get; set; }
    public bool CountAsMissedIncoming { get; set; }
    public bool CountAsOutgoingNoAnswer { get; set; }
}