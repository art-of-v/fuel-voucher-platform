using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Features.QrCodes;

[ApiController]
[Route("api/qr-codes")]
public class QrCodeBulkController : ControllerBase
{
    private readonly IQrCodeRepository _repository;

    public QrCodeBulkController(IQrCodeRepository repository)
    {
        _repository = repository;
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> BulkCreate([FromBody] BulkQrCodeRequest request)
    {
        if (request.QrCodes == null || request.QrCodes.Count == 0)
            return BadRequest(new { error = "qrCodes must be an array" });

        var created = new List<QrCode>();
        foreach (var qr in request.QrCodes)
        {
            var qrCode = await _repository.CreateAsync(qr);
            created.Add(qrCode);
        }

        return Ok(new { count = created.Count, qrCodes = created });
    }
}

public record BulkQrCodeRequest
{
    public List<CreateQrCodeRequest> QrCodes { get; init; } = new();
}
