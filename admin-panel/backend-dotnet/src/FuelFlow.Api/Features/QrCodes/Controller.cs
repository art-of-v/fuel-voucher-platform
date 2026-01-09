using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Features.QrCodes;

[ApiController]
[Route("api/admin/qr-codes")]
public class QrCodesController : ControllerBase
{
    private readonly IQrCodeRepository _repository;

    public QrCodesController(IQrCodeRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var qrCodes = await _repository.GetAllAsync();
        return Ok(qrCodes);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var qrCode = await _repository.GetByIdAsync(id);
        if (qrCode == null)
            return NotFound(new { message = "QR code not found" });
        return Ok(qrCode);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateQrCodeRequest request)
    {
        var qrCode = await _repository.CreateAsync(request);
        return Ok(qrCode);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _repository.DeleteAsync(id);
        if (!deleted)
            return NotFound(new { message = "QR code not found" });
        return Ok(new { message = "QR code deleted" });
    }
}

[ApiController]
[Route("api/qr-codes")]
public class PublicQrCodesController : ControllerBase
{
    private readonly IQrCodeRepository _repository;

    public PublicQrCodesController(IQrCodeRepository repository)
    {
        _repository = repository;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateQrCodeRequest request)
    {
        var qrCode = await _repository.CreateAsync(request);
        return Ok(qrCode);
    }
}
