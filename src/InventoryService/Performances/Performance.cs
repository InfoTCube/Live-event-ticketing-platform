namespace InventoryService.Performances;

public class Performance
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid VenueId { get; set; }
    public DateTime StartsAtUtc { get; set; }
}
