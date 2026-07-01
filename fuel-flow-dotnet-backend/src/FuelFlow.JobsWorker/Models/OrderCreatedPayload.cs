namespace FuelFlow.JobsWorker.Models
{
    public sealed class OrderCreatedPayload
    {
        public Guid OrderId { get; set; }
        public string UserId { get; set; } = null!;
        public string Provider { get; set; } = null!;
        public string FuelTypeId { get; set; } = null!;
        public int Liters { get; set; }
        public int Quantity { get; set; }
    }
}
