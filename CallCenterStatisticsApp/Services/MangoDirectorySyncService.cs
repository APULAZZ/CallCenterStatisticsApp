using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Models;
using Microsoft.EntityFrameworkCore;

namespace CallCenterStatisticsApp.Services;

public class MangoDirectorySyncService
{
    private readonly AppDbContext _db;
    private readonly IMangoApiClient _api;

    public MangoDirectorySyncService(AppDbContext db, IMangoApiClient api)
    {
        _db = db;
        _api = api;
    }

    public async Task SyncEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var items = await _api.GetUsersAsync(cancellationToken);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                continue;

            var employee = await _db.Employees
                .FirstOrDefaultAsync(x => x.MangoUserId == item.Id, cancellationToken);

            if (employee == null)
            {
                employee = new Employee
                {
                    MangoUserId = item.Id,
                    FullName = item.Name ?? string.Empty,
                    Extension = item.Extension,
                    IsActive = true
                };

                _db.Employees.Add(employee);
            }
            else
            {
                employee.FullName = item.Name ?? employee.FullName;
                employee.Extension = item.Extension;
                employee.IsActive = true;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncGroupsAsync(CancellationToken cancellationToken = default)
    {
        var items = await _api.GetGroupsAsync(cancellationToken);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                continue;

            var group = await _db.CallGroups
                .FirstOrDefaultAsync(x => x.MangoGroupId == item.Id, cancellationToken);

            if (group == null)
            {
                group = new CallGroup
                {
                    MangoGroupId = item.Id,
                    Name = item.Name ?? string.Empty,
                    IsActive = true
                };

                _db.CallGroups.Add(group);
            }
            else
            {
                group.Name = item.Name ?? group.Name;
                group.IsActive = true;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncTopicsAsync(CancellationToken cancellationToken = default)
    {
        var items = await _api.GetTopicsAsync(cancellationToken);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                continue;

            var topic = await _db.CallTopics
                .FirstOrDefaultAsync(x => x.MangoTopicId == item.Id, cancellationToken);

            if (topic == null)
            {
                topic = new CallTopic
                {
                    MangoTopicId = item.Id,
                    Name = item.Name ?? string.Empty,
                    IsActive = true
                };

                _db.CallTopics.Add(topic);
            }
            else
            {
                topic.Name = item.Name ?? topic.Name;
                topic.IsActive = true;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}