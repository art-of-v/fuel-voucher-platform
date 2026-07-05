using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Sync.GetSync;

public sealed class GetSyncCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly GetUserPurchasesCommandHandler _getUserPurchasesHandler;

    public GetSyncCommandHandler(
        ApplicationDbContext context,
        GetUserPurchasesCommandHandler getUserPurchasesHandler)
    {
        _context = context;
        _getUserPurchasesHandler = getUserPurchasesHandler;
    }

    public async Task<GetSyncResponse> HandleAsync(
        GetSyncCommand command,
        CancellationToken cancellationToken = default)
    {
        var purchasesCommand = new GetUserPurchasesCommand(command.UserId);
        var orders = await _getUserPurchasesHandler.HandleAsync(purchasesCommand, cancellationToken);

        var totalVouchers = orders.Sum(o => o.Vouchers?.Count ?? 0);

        return new GetSyncResponse(
            orders,
            orders.Count,
            totalVouchers
        );
    }
}
