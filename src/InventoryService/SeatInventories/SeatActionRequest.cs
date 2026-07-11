namespace InventoryService.SeatInventories;

public record SeatActionRequest(IReadOnlyList<Guid> SeatIds);
