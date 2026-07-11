namespace InventoryService.SeatInventories;

public class SeatInventory
{
    public Guid Id { get; set; }
    public Guid PerformanceId { get; set; }
    public Guid SeatId { get; set; }
    public SeatStatus Status { get; set; }
}
