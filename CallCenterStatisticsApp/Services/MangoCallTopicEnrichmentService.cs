using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Models;
using Microsoft.EntityFrameworkCore;

namespace CallCenterStatisticsApp.Services;

public class MangoCallTopicEnrichmentService
{
    private readonly AppDbContext _db;
    private readonly IMangoApiClient _api;

    public MangoCallTopicEnrichmentService(AppDbContext db, IMangoApiClient api)
    {
        _db = db;
        _api = api;
    }

    public async Task<int> EnrichTopicsAsync(int batchSize = 50, CancellationToken cancellationToken = default)
    {
        var calls = await _db.CallRecords
            .Include(x => x.Topic)
            .Where(x => x.TopicId == null &&
                        x.RecordingId != null &&
                        x.RecordingId != "")
            .OrderByDescending(x => x.CallDateTime)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var topics = await _db.CallTopics.ToListAsync(cancellationToken);

        int updated = 0;

        foreach (var call in calls)
        {
            if (string.IsNullOrWhiteSpace(call.RecordingId))
                continue;

            try
            {
                var categories = await _api.GetRecordingCategoriesAsync(call.RecordingId, cancellationToken);

                var firstCategory = categories.FirstOrDefault();
                if (firstCategory == null)
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                CallTopic? topic = null;

                if (firstCategory.CategoryId.HasValue)
                {
                    topic = topics.FirstOrDefault(x =>
                        !string.IsNullOrWhiteSpace(x.MangoTopicId) &&
                        x.MangoTopicId == firstCategory.CategoryId.Value.ToString());
                }

                if (topic == null && !string.IsNullOrWhiteSpace(firstCategory.CategoryName))
                {
                    topic = topics.FirstOrDefault(x =>
                        string.Equals(x.Name, firstCategory.CategoryName, StringComparison.OrdinalIgnoreCase));
                }

                if (topic != null)
                {
                    call.TopicId = topic.Id;
                    updated++;
                }

                await Task.Delay(500, cancellationToken);
            }
            catch
            {
                // Пока просто пропускаем проблемные записи, чтобы не ронять весь batch.
                await Task.Delay(1000, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return updated;
    }
}