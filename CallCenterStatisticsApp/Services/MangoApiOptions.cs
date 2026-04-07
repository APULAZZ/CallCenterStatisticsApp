namespace CallCenterStatisticsApp.Services;

public class MangoApiOptions
{
    public string BaseUrl { get; set; } = "https://app.mango-office.ru";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSalt { get; set; } = string.Empty;
}