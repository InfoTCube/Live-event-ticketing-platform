namespace InventoryService.SeatInventories;

public record SeatAvailabilityResponse(
    Guid SeatId,
    string Section,
    string Row,
    int Number,
    string Status);
