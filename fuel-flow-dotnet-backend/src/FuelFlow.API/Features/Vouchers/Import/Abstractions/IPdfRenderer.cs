namespace FuelFlow.Features.Vouchers.Import;

public interface IPdfRenderer
{
    Task<IReadOnlyList<PageRender>> RenderPagesAsync(Stream pdfStream, CancellationToken cancellationToken);
}
