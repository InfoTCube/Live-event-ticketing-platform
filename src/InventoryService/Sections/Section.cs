namespace InventoryService.Sections;

public class Section
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public string Name { get; set; } = "";
}
