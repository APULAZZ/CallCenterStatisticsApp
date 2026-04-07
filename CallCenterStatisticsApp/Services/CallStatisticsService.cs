using CallCenterStatisticsApp.Data;
using Microsoft.EntityFrameworkCore;

namespace CallCenterStatisticsApp.Services;

public class CallStatisticsService
{
    private readonly AppDbContext _db;

    public CallStatisticsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<EmployeeStatRow>> GetEmployeeStatsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CallRecords
            .AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Group)
            .Include(x => x.Topic)
            .Where(x => x.CallDateTime >= from && x.CallDateTime <= to);

        return await query
            .GroupBy(x => new
            {
                x.EmployeeId,
                EmployeeName = x.Employee != null ? x.Employee.FullName : "Не определен"
            })
            .Select(g => new EmployeeStatRow
            {
                EmployeeId = g.Key.EmployeeId,
                EmployeeName = g.Key.EmployeeName,

                IncomingCount = g.Count(x => x.IsIncoming),
                IncomingMissedCount = g.Count(x => x.IsMissedIncoming),
                OutgoingCount = g.Count(x => x.IsOutgoing),
                OutgoingNoAnswerCount = g.Count(x => x.IsOutgoingNoAnswer)
            })
            .OrderBy(x => x.EmployeeName)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<GroupStatRow>> GetGroupStatsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CallRecords
            .AsNoTracking()
            .Include(x => x.Group)
            .Where(x => x.CallDateTime >= from && x.CallDateTime <= to);

        return await query
            .GroupBy(x => new
            {
                x.GroupId,
                GroupName = x.Group != null ? x.Group.Name : "Не определена"
            })
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