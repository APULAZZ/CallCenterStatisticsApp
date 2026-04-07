namespace CallCenterStatisticsApp.Services;

public interface IMangoApiClient
{
    Task<List<MangoUserDto>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<List<MangoGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<List<MangoTopicDto>> GetTopicsAsync(CancellationToken cancellationToken = default);
    Task<List<MangoCallDto>> GetCallsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);


    Task<string> GetUsersRawAsync(CancellationToken cancellationToken = default);
    Task<string> GetGroupsRawAsync(CancellationToken cancellationToken = default);
    Task<string> GetTopicsRawAsync(CancellationToken cancellationToken = default);
    Task<string> GetCallsRawAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<List<MangoRecordingCategoryDto>> GetRecordingCategoriesAsync(
    string recordingId,
    CancellationToken cancellationToken = default);
}