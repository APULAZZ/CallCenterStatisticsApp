using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Models;
using Microsoft.EntityFrameworkCore;

namespace CallCenterStatisticsApp.Services;

public class MangoCallImportService
{
    private readonly AppDbContext _db;
    private readonly IMangoApiClient _api;

    public MangoCallImportService(AppDbContext db, IMangoApiClient api)
    {
        _db = db;
        _api = api;
    }

    public async Task ImportCallsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var syncLog = new MangoSyncLog
        {
            SyncType = "Calls",
            StartedAt = DateTime.Now,
            PeriodFrom = from,
            PeriodTo = to,
            IsSuccess = false
        };

        _db.MangoSyncLogs.Add(syncLog);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var calls = await _api.GetCallsAsync(from, to, cancellationToken);

            var employees = await _db.Employees.ToListAsync(cancellationToken);
            var groups = await _db.CallGroups.ToListAsync(cancellationToken);
            var topics = await _db.CallTopics.ToListAsync(cancellationToken);

            var existingCallIds = await _db.CallRecords
                .Where(x => x.CallDateTime >= from.AddDays(-1) && x.CallDateTime <= to.AddDays(1))
                .Select(x => x.MangoCallId)
                .ToListAsync(cancellationToken);

            var existingCallIdSet = existingCallIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

            int importedCount = 0;
            int skippedCount = 0;
            int updatedCount = 0;

            foreach (var dto in calls)
            {
                if (string.IsNullOrWhiteSpace(dto.CallId))
                {
                    skippedCount++;
                    continue;
                }

                if (existingCallIdSet.Contains(dto.CallId))
                {
                    skippedCount++;
                    continue;
                }

                var employee = await FindOrCreateEmployeeAsync(employees, dto, cancellationToken);
                var group = await FindOrCreateGroupAsync(groups, dto, cancellationToken);
                var topic = FindTopic(topics, dto);

                var record = new CallRecord
                {
                    MangoCallId = dto.CallId,
                    CallDateTime = dto.CallDateTime == DateTime.MinValue ? DateTime.Now : dto.CallDateTime,

                    EmployeeId = employee?.Id,
                    GroupId = group?.Id,
                    TopicId = topic?.Id,

                    ExternalPhoneNumber = dto.PhoneNumber,
                    Direction = dto.Direction ?? string.Empty,
                    StatusCode = dto.StatusCode,
                    StatusText = dto.StatusText,
                    RecordingId = dto.RecordingId,

                    DurationSeconds = dto.DurationSeconds,
                    TalkDurationSeconds = dto.TalkDurationSeconds,
                    WaitDurationSeconds = CalculateWaitDuration(dto),

                    IsIncoming = IsIncoming(dto),
                    IsOutgoing = IsOutgoing(dto),
                    IsAnswered = IsAnswered(dto),
                    IsMissedIncoming = IsMissedIncoming(dto),
                    IsOutgoingNoAnswer = IsOutgoingNoAnswer(dto),

                    RawJson = dto.RawJson,
                    ImportedAt = DateTime.Now
                };

                _db.CallRecords.Add(record);
                existingCallIdSet.Add(dto.CallId);
                importedCount++;
            }

            updatedCount = await _db.SaveChangesAsync(cancellationToken);

            syncLog.ImportedCount = importedCount;
            syncLog.SkippedCount = skippedCount;
            syncLog.UpdatedCount = updatedCount;
            syncLog.IsSuccess = true;
            syncLog.FinishedAt = DateTime.Now;

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            syncLog.IsSuccess = false;
            syncLog.FinishedAt = DateTime.Now;
            syncLog.ErrorText = ex.ToString();

            await _db.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    private async Task<Employee?> FindOrCreateEmployeeAsync(List<Employee> employees, MangoCallDto dto, CancellationToken cancellationToken)
    {
        Employee? employee = null;

        // 1. Ищем по MangoUserId
        if (!string.IsNullOrWhiteSpace(dto.EmployeeMangoId))
        {
            employee = employees.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.MangoUserId) &&
                string.Equals(x.MangoUserId, dto.EmployeeMangoId, StringComparison.OrdinalIgnoreCase));
        }

        // 2. Если не нашли — по extension
        if (employee == null && !string.IsNullOrWhiteSpace(dto.EmployeeExtension))
        {
            employee = employees.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.Extension) &&
                string.Equals(x.Extension, dto.EmployeeExtension, StringComparison.OrdinalIgnoreCase));
        }

        // 3. Если нашли по extension, но MangoUserId пустой — дополняем
        if (employee != null)
        {
            bool changed = false;

            if (string.IsNullOrWhiteSpace(employee.MangoUserId) && !string.IsNullOrWhiteSpace(dto.EmployeeMangoId))
            {
                employee.MangoUserId = dto.EmployeeMangoId;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(employee.Extension) && !string.IsNullOrWhiteSpace(dto.EmployeeExtension))
            {
                employee.Extension = dto.EmployeeExtension;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(employee.FullName) && !string.IsNullOrWhiteSpace(dto.EmployeeName))
            {
                employee.FullName = dto.EmployeeName;
                changed = true;
            }

            if (changed)
            {
                _db.Employees.Update(employee);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return employee;
        }

        // 4. Если не нашли вообще, но есть данные — создаем нового сотрудника
        if (!string.IsNullOrWhiteSpace(dto.EmployeeMangoId) ||
            !string.IsNullOrWhiteSpace(dto.EmployeeExtension) ||
            !string.IsNullOrWhiteSpace(dto.EmployeeName))
        {
            employee = new Employee
            {
                MangoUserId = dto.EmployeeMangoId,
                Extension = dto.EmployeeExtension,
                FullName = dto.EmployeeName ?? dto.EmployeeExtension ?? dto.EmployeeMangoId ?? "Неизвестный сотрудник",
                IsActive = true
            };

            _db.Employees.Add(employee);
            await _db.SaveChangesAsync(cancellationToken);
            employees.Add(employee);

            return employee;
        }

        return null;
    }

    private async Task<CallGroup?> FindOrCreateGroupAsync(List<CallGroup> groups, MangoCallDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.GroupMangoId))
            return null;

        var group = groups.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(x.MangoGroupId) &&
            string.Equals(x.MangoGroupId, dto.GroupMangoId, StringComparison.OrdinalIgnoreCase));

        if (group != null)
        {
            if (string.IsNullOrWhiteSpace(group.Name) && !string.IsNullOrWhiteSpace(dto.GroupName))
            {
                group.Name = dto.GroupName;
                _db.CallGroups.Update(group);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return group;
        }

        group = new CallGroup
        {
            MangoGroupId = dto.GroupMangoId,
            Name = dto.GroupName ?? $"Группа {dto.GroupMangoId}",
            IsActive = true
        };

        _db.CallGroups.Add(group);
        await _db.SaveChangesAsync(cancellationToken);
        groups.Add(group);

        return group;
    }

    private static CallTopic? FindTopic(List<CallTopic> topics, MangoCallDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TopicMangoId))
            return null;

        return topics.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(x.MangoTopicId) &&
            string.Equals(x.MangoTopicId, dto.TopicMangoId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIncoming(MangoCallDto dto)
        => string.Equals(dto.Direction, "incoming", StringComparison.OrdinalIgnoreCase);

    private static bool IsOutgoing(MangoCallDto dto)
        => string.Equals(dto.Direction, "outgoing", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnswered(MangoCallDto dto)
    {
        return string.Equals(dto.StatusCode, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(dto.StatusText, "successful", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissedIncoming(MangoCallDto dto)
    {
        return IsIncoming(dto) &&
               (
                   string.Equals(dto.StatusCode, "0", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dto.StatusText, "unsuccessful", StringComparison.OrdinalIgnoreCase)
               );
    }

    private static bool IsOutgoingNoAnswer(MangoCallDto dto)
    {
        // Пока MVP:
        // любой неуспешный исходящий считаем исходящим без ответа.
        // Потом можно ужесточить через CallEndReason.
        return IsOutgoing(dto) &&
               (
                   string.Equals(dto.StatusCode, "0", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dto.StatusText, "unsuccessful", StringComparison.OrdinalIgnoreCase)
               );
    }

    private static int? CalculateWaitDuration(MangoCallDto dto)
    {
        if (!dto.DurationSeconds.HasValue || !dto.TalkDurationSeconds.HasValue)
            return null;

        var wait = dto.DurationSeconds.Value - dto.TalkDurationSeconds.Value;
        return wait >= 0 ? wait : null;
    }
}