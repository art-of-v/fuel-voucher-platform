namespace FuelFlow.Features.Vouchers.Import;

public interface IPdfRenderer
{
    IAsyncEnumerable<PageRender> RenderPagesAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}
