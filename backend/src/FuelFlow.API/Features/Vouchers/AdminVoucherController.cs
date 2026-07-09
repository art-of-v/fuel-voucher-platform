using FuelFlow.Features.Vouchers.GetVoucherVerification;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/vouchers")]
[Authorize(Roles = "Admin")]
public sealed class AdminVoucherController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IQrGenerator _qrGenerator;
    private readonly GetVoucherVerificationQueryHandler _getVerificationHandler;

    public AdminVoucherController(
        ApplicationDbContext dbContext,
        IQrGenerator qrGenerator,
        GetVoucherVerificationQueryHandler getVerificationHandler)
    {
        _dbContext = dbContext;
        _qrGenerator = qrGenerator;
        _getVerificationHandler = getVerificationHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        [FromQuery] string? fuelType = null,
        [FromQuery] string? status = null,
        [FromQuery] string? provider = null,
        [FromQuery] string? amount = null,
        [FromQuery] string? expirationDate = null)
    {
        var query = _dbContext.FuelVouchers
            .AsNoTracking()
            .Include(v => v.FuelType)
            .Include(v => v.QrParameters)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<VoucherStatus>(status, true, out var parsedStatus))
            query = query.Where(v => v.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(provider))
            query = query.Where(v => v.Provider == provider);

        if (!string.IsNullOrWhiteSpace(fuelType))
            query = query.Where(v => v.FuelType!.Name == fuelType);

        if (!string.IsNullOrWhiteSpace(amount) && decimal.TryParse(amount, out var parsedAmount))
            query = query.Where(v => v.Liters == parsedAmount);

        if (!string.IsNullOrWhiteSpace(expirationDate) && DateOnly.TryParse(expirationDate, out var parsedDate))
            query = query.Where(v => v.ExpirationDate == parsedDate);

        var globalTotal = await _dbContext.FuelVouchers.CountAsync(cancellationToken);
        var total = await query.CountAsync(cancellationToken);

        IOrderedQueryable<FuelVoucher> ordered = sortBy switch
        {
            "createdAt" => sortDirection == "asc"
                ? query.OrderBy(v => v.CreatedAtUtc)
                : query.OrderByDescending(v => v.CreatedAtUtc),
            "expirationDate" => sortDirection == "asc"
                ? query.OrderBy(v => v.ExpirationDate)
                : query.OrderByDescending(v => v.ExpirationDate),
            "amount" => sortDirection == "asc"
                ? query.OrderBy(v => v.Liters)
                : query.OrderByDescending(v => v.Liters),
            "provider" => sortDirection == "asc"
                ? query.OrderBy(v => v.Provider)
                : query.OrderByDescending(v => v.Provider),
            "fuelType" => sortDirection == "asc"
                ? query.OrderBy(v => v.FuelType!.Name)
                : query.OrderByDescending(v => v.FuelType!.Name),
            "status" => sortDirection == "asc"
                ? query.OrderBy(v => v.Status)
                : query.OrderByDescending(v => v.Status),
            _ => query.OrderByDescending(v => v.CreatedAtUtc)
        };

        var items = await ordered
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var data = items.Select(v => new
        {
            id = v.Id,
            qrPayload = v.QrPayload,
            liters = v.Liters,
            fuelTypeId = v.FuelTypeId,
            fuelType = v.FuelType == null ? null : new { id = v.FuelType.Id, name = v.FuelType.Name },
            provider = v.Provider,
            expirationDate = v.ExpirationDate,
            voucherNumber = v.VoucherNumber,
            status = v.Status,
            createdAtUtc = v.CreatedAtUtc,
            imageUrl = v.ImageUrl,
            qrImage = GenerateQrImage(v.QrPayload, v.QrParameters)
        });

        var fuelTypes = await _dbContext.FuelTypes
            .AsNoTracking()
            .Select(ft => ft.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

        var providers = await _dbContext.FuelVouchers
            .AsNoTracking()
            .Select(v => v.Provider)
            .Distinct()
            .ToListAsync(cancellationToken);

        var statuses = new[] { "Imported", "Available", "Assigned", "Used", "Expired" };
        var amounts = await _dbContext.FuelVouchers
            .AsNoTracking()
            .Select(v => v.Liters)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            data,
            total,
            globalTotal,
            fuelTypes,
            providers,
            statuses,
            amounts
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var item = await _dbContext.FuelVouchers
            .AsNoTracking()
            .Include(v => v.FuelType)
            .Include(v => v.QrParameters)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (item is null) return NotFound();

        return Ok(new
        {
            id = item.Id,
            qrPayload = item.QrPayload,
            liters = item.Liters,
            fuelTypeId = item.FuelTypeId,
            fuelType = item.FuelType == null ? null : new { id = item.FuelType.Id, name = item.FuelType.Name },
            provider = item.Provider,
            expirationDate = item.ExpirationDate,
            voucherNumber = item.VoucherNumber,
            status = item.Status,
            createdAtUtc = item.CreatedAtUtc,
            imageUrl = item.ImageUrl,
            qrImage = GenerateQrImage(item.QrPayload, item.QrParameters)
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateVoucherRequest request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FuelVouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<VoucherStatus>(request.Status, out var parsedStatus))
            entity.Status = parsedStatus;
        if (request.AssignedToUserId.HasValue)
            entity.AssignedToUserId = request.AssignedToUserId.Value.ToString();

        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FuelVouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        _dbContext.FuelVouchers.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true });
    }

    [HttpPost("bulk-action")]
    public async Task<IActionResult> BulkAction([FromBody] BulkActionRequest request, CancellationToken cancellationToken)
    {
        if (request.Action == "delete_all")
        {
            _dbContext.FuelVouchers.RemoveRange(await _dbContext.FuelVouchers.ToListAsync(cancellationToken));
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new { success = true });
        }

        if (request.Ids is null || request.Ids.Count == 0)
            return BadRequest(new { error = "No IDs provided" });

        var entities = await _dbContext.FuelVouchers
            .Where(v => request.Ids.Contains(v.Id))
            .ToListAsync(cancellationToken);

        switch (request.Action)
        {
            case "activate":
                foreach (var e in entities) { e.Status = VoucherStatus.Available; e.UpdatedAtUtc = DateTime.UtcNow; }
                break;
            case "expire":
                foreach (var e in entities) { e.Status = VoucherStatus.Expired; e.UpdatedAtUtc = DateTime.UtcNow; }
                break;
            case "assign":
                if (string.IsNullOrWhiteSpace(request.TargetUserId))
                    return BadRequest(new { error = "Target User ID required" });
                foreach (var e in entities) { e.Status = VoucherStatus.Assigned; e.AssignedToUserId = request.TargetUserId; e.UpdatedAtUtc = DateTime.UtcNow; }
                break;
            case "delete":
                _dbContext.FuelVouchers.RemoveRange(entities);
                break;
            default:
                return BadRequest(new { error = $"Unknown action: {request.Action}" });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, count = entities.Count });
    }

    [HttpGet("{id:guid}/verification")]
    [ProducesResponseType(typeof(GetVoucherVerificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVoucherVerification(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getVerificationHandler.HandleAsync(new GetVoucherVerificationQuery(id), cancellationToken);
        if (result == null)
            return NotFound();

        return Ok(result);
    }

    private string? GenerateQrImage(string? qrPayload, QrParameters? qrParams)
    {
        if (string.IsNullOrWhiteSpace(qrPayload))
            return null;

        return "data:image/png;base64," + _qrGenerator.GenerateQrCode(
            qrPayload,
            eccLevel: qrParams?.EccLevel,
            version: qrParams?.Version,
            encodingMode: qrParams?.EncodingMode,
            maskPattern: qrParams?.MaskPattern);
    }
}

public sealed class UpdateVoucherRequest
{
    public string? Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
}

public sealed class BulkActionRequest
{
    public string Action { get; set; } = null!;
    public List<Guid>? Ids { get; set; }
    public string? TargetUserId { get; set; }
}
