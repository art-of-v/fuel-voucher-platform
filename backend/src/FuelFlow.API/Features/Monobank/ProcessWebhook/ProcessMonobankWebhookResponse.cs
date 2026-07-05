namespace FuelFlow.API.Features.Monobank.ProcessWebhook
{
    public sealed class ProcessMonobankWebhookResponse
    {
        public bool Success { get; set; }
        public string? OrderId { get; set; }
        public string? PreviousStatus { get; set; }
        public string? NewStatus { get; set; }
        public string Message { get; set; } = string.Empty;
    }

}
