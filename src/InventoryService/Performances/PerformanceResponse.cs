namespace InventoryService.Performances;

public record PerformanceResponse(
    Guid Id,
    Guid EventId,
    string EventName,
    Guid VenueId,
    string VenueName,
    DateTime StartsAtUtc);
