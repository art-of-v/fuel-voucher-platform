using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/qr-codes")]
[Authorize(Roles = "Admin")]
public sealed class AdminQrCodeController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminQrCodeController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _dbContext.FuelVouchers
            .AsNoTracking()
            .Include(v => v.FuelType)
            .OrderByDescending(v => v.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var result = items.Select((v, i) => new
        {
            id = i + 1,
            stationId = v.FuelType?.StationId ?? "",
            fuelType = v.FuelType?.Name ?? v.FuelTypeId,
            liters = v.Liters,
            qrCodeUrl = v.QrPayload,
            status = v.Status == VoucherStatus.Available ? "available" : "sold"
        });

        return Ok(result);
    }
}
