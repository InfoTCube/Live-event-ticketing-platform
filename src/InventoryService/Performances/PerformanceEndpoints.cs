using InventoryService.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Performances;

public static class PerformanceEndpoints
{
    public static IEndpointRouteBuilder MapPerformanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/performances", async (InventoryDbContext db) =>
        {
            var performances = await (
                from p in db.Performances
                join e in db.Events on p.EventId equals e.Id
                join v in db.Venues on p.VenueId equals v.Id
                select new PerformanceResponse(p.Id, p.EventId, e.Name, p.VenueId, v.Name, p.StartsAtUtc))
                .ToListAsync();
            return Results.Ok(performances);
        });

        return app;
    }
}
