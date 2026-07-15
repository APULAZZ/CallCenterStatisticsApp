namespace CallCenterStatisticsApp.Services;

public sealed class MangoSynchronizationService
{
    private readonly MangoDirectorySyncService _directory;
    private readonly MangoCallImportService _calls;

    public MangoSynchronizationService(MangoDirectorySyncService directory, MangoCallImportService calls)
    {
        _directory = directory;
        _calls = calls;
    }

    public async Task<string> SynchronizeAsync(DateTime from, DateTime to, bool syncEmployees, bool syncTopics, CancellationToken cancellationToken = default)
    {
        var completed = new List<string>();
        if (syncEmployees) { await _directory.SyncEmployeesAsync(cancellationToken); completed.Add("сотрудники"); }
        if (syncTopics) { await _directory.SyncTopicsAsync(cancellationToken); completed.Add("тематики"); }
        await _calls.ImportCallsAsync(from, to, cancellationToken);
        completed.Add("звонки");
        return $"Синхронизация завершена: {string.Join(", ", completed)}.";
    }
}
