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

            var existingRecords = await _db.CallRecords
                .Where(x => x.CallDateTime >= from.AddDays(-1) && x.CallDateTime <= to.AddDays(1))
                .ToDictionaryAsync(x => x.MangoCallId, StringComparer.OrdinalIgnoreCase, cancellationToken);

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

                // The call-statistics report does not always include the manual Contact Center topic.
                // For answered calls MANGO exposes it through /vpbx/cc/call/ by entry_id.
                if (string.IsNullOrWhiteSpace(dto.TopicMangoId) && IsAnswered(dto))
                {
                    try
                    {
                        dto.TopicMangoId = await _api.GetCallTopicIdAsync(dto.CallId, cancellationToken);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        // A regular PBX call may not have Contact Center data. Do not stop the whole import.
                    }

                }

                var employee = await FindOrCreateEmployeeAsync(employees, dto, cancellationToken);
                var group = await FindOrCreateGroupAsync(groups, dto, cancellationToken);
                var topic = FindTopic(topics, dto);

                if (existingRecords.TryGetValue(dto.CallId, out var record))
                {
                    ApplyDto(record, dto, employee, group, topic);
                    updatedCount++;
                }
                else
                {
                    record = new CallRecord { MangoCallId = dto.CallId };
                    ApplyDto(record, dto, employee, group, topic);
                    _db.CallRecords.Add(record);
                    existingRecords.Add(dto.CallId, record);
                    importedCount++;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);

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

    /// <summary>
    /// Uses locally completed days as a cache. The current day is always
    /// refreshed because calls and their post-call data can still change.
    /// </summary>
    public async Task EnsurePeriodImportedAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            var dayStart = day;
            var dayEnd = day.AddDays(1).AddSeconds(-1);
            var isToday = day == DateTime.Today;
            var isAlreadyImported = !isToday && await _db.MangoSyncLogs.AsNoTracking().AnyAsync(x =>
                x.SyncType == "Calls" && x.IsSuccess && x.PeriodFrom <= dayStart && x.PeriodTo >= dayEnd,
                cancellationToken);

            if (!isAlreadyImported)
                await ImportCallsAsync(dayStart, dayEnd, cancellationToken);
        }
    }

    private static void ApplyDto(
        CallRecord record,
        MangoCallDto dto,
        Employee? employee,
        CallGroup? group,
        CallTopic? topic)
    {
        if (dto.CallDateTime != DateTime.MinValue)
            record.CallDateTime = dto.CallDateTime;

        record.EmployeeId = employee?.Id ?? record.EmployeeId;
        record.GroupId = group?.Id ?? record.GroupId;
        record.TopicId = topic?.Id ?? record.TopicId;
        record.ExternalPhoneNumber = dto.PhoneNumber ?? record.ExternalPhoneNumber;
        record.Direction = dto.Direction ?? record.Direction;
        record.StatusCode = dto.StatusCode ?? record.StatusCode;
        record.StatusText = dto.StatusText ?? record.StatusText;
        record.RecordingId = dto.RecordingId ?? record.RecordingId;
        record.DurationSeconds = dto.DurationSeconds ?? record.DurationSeconds;
        record.TalkDurationSeconds = dto.TalkDurationSeconds ?? record.TalkDurationSeconds;
        record.WaitDurationSeconds = CalculateWaitDuration(dto) ?? record.WaitDurationSeconds;
        record.IsIncoming = IsIncoming(dto);
        record.IsOutgoing = IsOutgoing(dto);
        record.IsAnswered = IsAnswered(dto);
        record.IsMissedIncoming = IsMissedIncoming(dto);
        record.IsOutgoingNoAnswer = IsOutgoingNoAnswer(dto);
        record.RawJson = dto.RawJson;
        record.ImportedAt = DateTime.Now;
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
