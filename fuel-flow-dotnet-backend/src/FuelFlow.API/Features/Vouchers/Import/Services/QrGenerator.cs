namespace FuelFlow.Features.Vouchers.Import;

public interface IQrGenerator
{
    string GenerateQrCode(
        string payload,
        int width = 300,
        int height = 300,
        string? eccLevel = null,
        int? version = null,
        string? encodingMode = null,
        int? maskPattern = null);
}
