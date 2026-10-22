using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.ErrorLogs.ClearErrorLogs;

/// <summary>
/// Empties the error journal, or the part of it that is older than a given instant.
/// </summary>
/// <remarks>
/// The delete is a bulk one, so it does not load the rows it removes: the journal is the table
/// that grows fastest in a system that misbehaves, and the rows are of no use to anybody once
/// they are gone.
/// </remarks>
public sealed class ClearErrorLogsCommandHandler
{
    private readonly ApplicationDbContext _context;

    public ClearErrorLogsCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<int> HandleAsync(ClearErrorLogsCommand command, CancellationToken ct = default)
    {
        IQueryable<ErrorLog> query = _context.ErrorLogs;
        if (command.Before.HasValue)
            query = query.Where(e => e.LoggedAtUtc <= command.Before.Value);

        var deleted = await query.ExecuteDeleteAsync(ct);

        return deleted;
    }
}
