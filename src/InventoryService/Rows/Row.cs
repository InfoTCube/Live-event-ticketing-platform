namespace InventoryService.Rows;

public class Row
{
    public Guid Id { get; set; }
    public Guid SectionId { get; set; }
    public string Label { get; set; } = "";
}
