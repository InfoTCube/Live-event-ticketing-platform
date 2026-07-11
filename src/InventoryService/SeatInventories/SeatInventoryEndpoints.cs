using InventoryService.Data;

namespace InventoryService.SeatInventories;

public static class SeatInventoryEndpoints
{
    public static IEndpointRouteBuilder MapSeatInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/performances/{performanceId:guid}/seats", async (Guid performanceId, InventoryDbContext db) =>
        {
            var (outcome, seats) = await SeatInventoryOperations.GetAvailability(db, performanceId);
            return outcome == SeatActionOutcome.NotFound ? Results.NotFound() : Results.Ok(seats);
        });

        app.MapPost("/api/v1/performances/{performanceId:guid}/hold", async (Guid performanceId, SeatActionRequest request, InventoryDbContext db) =>
            ToResult(await SeatInventoryOperations.Hold(db, performanceId, request.SeatIds)));

        app.MapPost("/api/v1/performances/{performanceId:guid}/release", async (Guid performanceId, SeatActionRequest request, InventoryDbContext db) =>
            ToResult(await SeatInventoryOperations.Release(db, performanceId, request.SeatIds)));

        app.MapPost("/api/v1/performances/{performanceId:guid}/sell", async (Guid performanceId, SeatActionRequest request, InventoryDbContext db) =>
            ToResult(await SeatInventoryOperations.Sell(db, performanceId, request.SeatIds)));

        return app;
    }

    static IResult ToResult(SeatActionOutcome outcome) => outcome switch
    {
        SeatActionOutcome.Ok => Results.Ok(),
        SeatActionOutcome.NotFound => Results.NotFound(),
        _ => Results.Conflict(),
    };
}
