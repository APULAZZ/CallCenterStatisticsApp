using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CallCenterStatisticsApp.Services;

public class CallStatisticsService
{
    private readonly AppDbContext _db;

    public CallStatisticsService(AppDbContext db) => _db = db;

    public async Task<List<EmployeeStatRow>> GetEmployeeStatsAsync(
        DateTime from,
        DateTime to,
        EmployeeStatisticsFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new EmployeeStatisticsFilter();

        IQueryable<Employee> employeesQuery = _db.Employees.AsNoTracking();
        if (filter.EmployeeIds is { Count: > 0 } && !filter.WithoutEmployees)
            employeesQuery = employeesQuery.Where(x => filter.EmployeeIds.Contains(x.Id));
        if (filter.WithoutEmployees)
            employeesQuery = employeesQuery.Where(_ => false);

        if (filter.LimitEmployeesToGroups && filter.GroupIds is { Count: > 0 })
        {
            var linkedEmployeeIds = await _db.EmployeeGroups.AsNoTracking()
                .Where(x => filter.GroupIds.Contains(x.GroupId))
                .Select(x => x.EmployeeId)
                .ToListAsync(cancellationToken);
            var historicalEmployeeIds = await _db.CallRecords.AsNoTracking()
                .Where(x => x.GroupId.HasValue && filter.GroupIds.Contains(x.GroupId.Value) && x.EmployeeId.HasValue)
                .Select(x => x.EmployeeId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
            var groupMemberEmployeeIds = linkedEmployeeIds.Concat(historicalEmployeeIds).Distinct().ToList();
            var isCallCenterSelected = await _db.CallGroups.AsNoTracking()
                .AnyAsync(x => filter.GroupIds.Contains(x.Id) && x.Name == "Коллцентр", cancellationToken);
            employeesQuery = isCallCenterSelected
                ? employeesQuery.Where(x => groupMemberEmployeeIds.Contains(x.Id) || x.FullName.StartsWith("КЦ "))
                : employeesQuery.Where(x => groupMemberEmployeeIds.Contains(x.Id));
        }

        var employees = await employeesQuery.OrderBy(x => x.FullName).ToListAsync(cancellationToken);
        var employeeIdsByMangoId = await _db.Employees.AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.MangoUserId))
            .ToDictionaryAsync(x => x.MangoUserId!, x => x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var callsQuery = _db.CallRecords.AsNoTracking()
            .Where(x => x.CallDateTime >= from && x.CallDateTime <= to);

        if (filter.WithoutGroups)
            callsQuery = callsQuery.Where(x => x.GroupId == null);
        else if (filter.GroupIds is { Count: > 0 })
            callsQuery = callsQuery.Where(x => x.GroupId.HasValue && filter.GroupIds.Contains(x.GroupId.Value));

        if (!filter.IgnoreTopics)
        {
            if (filter.WithoutTopics)
                callsQuery = callsQuery.Where(x => x.TopicId == null);
            else if (filter.TopicIds is { Count: > 0 })
                callsQuery = callsQuery.Where(x => x.TopicId.HasValue && filter.TopicIds.Contains(x.TopicId.Value));
        }

        var callsWithEmployee = (await callsQuery.ToListAsync(cancellationToken))
            .Select(x => new { Call = x, EmployeeId = GetResponsibleEmployeeId(x, employeeIdsByMangoId) });

        if (filter.WithoutEmployees)
            callsWithEmployee = callsWithEmployee.Where(x => !x.EmployeeId.HasValue);
        else if (filter.EmployeeIds is { Count: > 0 })
            callsWithEmployee = callsWithEmployee.Where(x => x.EmployeeId.HasValue && filter.EmployeeIds.Contains(x.EmployeeId.Value));

        var callsByEmployee = callsWithEmployee
            .Where(x => x.EmployeeId.HasValue)
            .GroupBy(x => x.EmployeeId!.Value)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Call).ToList());

        var rows = employees
            .GroupBy(x => x.FullName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(employeeGroup =>
            {
                var employeeCalls = employeeGroup
                    .SelectMany(x => callsByEmployee.GetValueOrDefault(x.Id) ?? [])
                    .ToList();
                return CreateRow(employeeGroup.First().Id, employeeGroup.First().FullName, employeeCalls);
            })
            .OrderBy(x => x.EmployeeName)
            .ToList();

        if (filter.WithoutEmployees)
        {
            var unassignedCalls = callsWithEmployee.Where(x => !x.EmployeeId.HasValue).Select(x => x.Call).ToList();
            rows.Add(CreateRow(null, "Не определён", unassignedCalls));
        }

        rows.Add(CreateTotalRow(rows));
        return rows;
    }

    private static EmployeeStatRow CreateTotalRow(IEnumerable<EmployeeStatRow> rows)
        => new()
        {
            EmployeeName = "Всего",
            IsTotal = true,
            IncomingAcceptedCount = rows.Sum(x => x.IncomingAcceptedCount),
            IncomingAcceptedWithoutTransfersCount = rows.Sum(x => x.IncomingAcceptedWithoutTransfersCount),
            OutgoingCount = rows.Sum(x => x.OutgoingCount),
            OutgoingNoAnswerCount = rows.Sum(x => x.OutgoingNoAnswerCount),
            InternalOutgoingCount = rows.Sum(x => x.InternalOutgoingCount),
            TransfersCount = rows.Sum(x => x.TransfersCount),
            MissedCount = rows.Sum(x => x.MissedCount),
            TopicsTotalCount = rows.Sum(x => x.TopicsTotalCount),
            TopicsWithoutTransfersCount = rows.Sum(x => x.TopicsWithoutTransfersCount),
            TopicsInTransfersCount = rows.Sum(x => x.TopicsInTransfersCount)
        };

    private static EmployeeStatRow CreateRow(int? employeeId, string employeeName, IReadOnlyCollection<CallRecord> calls)
        => new()
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            IncomingAcceptedCount = calls.Count(x => x.IsIncoming && x.IsAnswered),
            IncomingAcceptedWithoutTransfersCount = calls.Count(x => x.IsIncoming && x.IsAnswered && !IsTransfer(x)),
            OutgoingCount = calls.Count(x => x.IsOutgoing),
            OutgoingNoAnswerCount = calls.Count(x => x.IsOutgoingNoAnswer),
            InternalOutgoingCount = calls.Count(x => string.Equals(x.Direction, "internal", StringComparison.OrdinalIgnoreCase)),
            TransfersCount = calls.Count(IsTransfer),
            MissedCount = calls.Count(x => x.IsMissedIncoming),
            TopicsTotalCount = calls.Count(x => x.TopicId.HasValue),
            TopicsWithoutTransfersCount = calls.Count(x => x.TopicId.HasValue && !IsTransfer(x)),
            TopicsInTransfersCount = calls.Count(x => x.TopicId.HasValue && IsTransfer(x))
        };

    private static int? GetResponsibleEmployeeId(CallRecord call, IReadOnlyDictionary<string, int> employeeIdsByMangoId)
    {
        if (!string.Equals(call.Direction, "internal", StringComparison.OrdinalIgnoreCase))
            return call.EmployeeId;

        try
        {
            using var document = JsonDocument.Parse(call.RawJson ?? "{}");
            if (!document.RootElement.TryGetProperty("caller_id", out var callerId)) return call.EmployeeId;
            var mangoUserId = callerId.ValueKind switch
            {
                JsonValueKind.String => callerId.GetString(),
                JsonValueKind.Number => callerId.ToString(),
                _ => null
            };
            return mangoUserId != null && employeeIdsByMangoId.TryGetValue(mangoUserId, out var employeeId) ? employeeId : call.EmployeeId;
        }
        catch (JsonException) { return call.EmployeeId; }
    }

    private static bool IsTransfer(CallRecord call)
        => call.RawJson?.Contains("\"BlindTransfer\":true", StringComparison.OrdinalIgnoreCase) == true
           || call.RawJson?.Contains("\"ConsultTransfer\":true", StringComparison.OrdinalIgnoreCase) == true;

    public async Task<List<GroupStatRow>> GetGroupStatsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var query = _db.CallRecords.AsNoTracking().Include(x => x.Group)
            .Where(x => x.CallDateTime >= from && x.CallDateTime <= to);

        return await query.GroupBy(x => new { x.GroupId, GroupName = x.Group != null ? x.Group.Name : "Не определена" })
            .Select(g => new GroupStatRow
            {
                GroupId = g.Key.GroupId,
                GroupName = g.Key.GroupName,
                IncomingCount = g.Count(x => x.IsIncoming),
                IncomingMissedCount = g.Count(x => x.IsMissedIncoming),
                OutgoingCount = g.Count(x => x.IsOutgoing),
                OutgoingNoAnswerCount = g.Count(x => x.IsOutgoingNoAnswer)
            })
            .OrderBy(x => x.GroupName)
            .ToListAsync(cancellationToken);
    }
}
