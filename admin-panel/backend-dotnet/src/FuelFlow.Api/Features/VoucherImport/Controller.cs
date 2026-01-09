using FuelFlow.Api.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Features.VoucherImport;

[ApiController]
[Route("api/vouchers")]
public class VoucherImportController : ControllerBase
{
    private readonly IImportJobRepository _repository;
    private readonly IImportOrchestrator _orchestrator;

    public VoucherImportController(IImportJobRepository repository, IImportOrchestrator orchestrator)
    {
        _repository = repository;
        _orchestrator = orchestrator;
    }

    [HttpPost]
    public async Task<IActionResult> Import(IFormFileCollection files)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(new { message = "No files uploaded" });
        }

        var job = await _repository.CreateAsync(files.Count);

        // Queue processing
        _orchestrator.QueueJob(job.Id.ToString(), files.ToList());

        return Ok(new
        {
            jobId = job.Id,
            message = "Import job created successfully",
            files = files.Count
        });
    }

    [HttpGet("import-status/{id}")]
    public async Task<IActionResult> GetStatus(string id)
    {
        var job = await _repository.GetByIdAsync(id);
        if (job == null)
        {
            return NotFound(new { message = "Import job not found" });
        }
        return Ok(job);
    }
}

[ApiController]
[Route("api/upload")]
public class UploadController : ControllerBase
{
    [HttpPost]
    public IActionResult Upload([FromForm] IFormFile file)
    {
        if (file == null)
            return BadRequest(new { error = "No file uploaded" });

        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        Directory.CreateDirectory(uploadsDir);

        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
        var filePath = Path.Combine(uploadsDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            file.CopyTo(stream);
        }

        return Ok(new { url = $"/uploads/{fileName}" });
    }
}
