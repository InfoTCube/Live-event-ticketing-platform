using InventoryService.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.SeatInventories;

public static class SeatInventoryOperations
{
    public static async Task<(SeatActionOutcome Outcome, IReadOnlyList<SeatAvailabilityResponse> Seats)> GetAvailability(
        InventoryDbContext db, Guid performanceId)
    {
        if (!await db.Performances.AnyAsync(p => p.Id == performanceId))
            return (SeatActionOutcome.NotFound, []);

        var rows = await (
            from si in db.SeatInventories
            where si.PerformanceId == performanceId
            join seat in db.Seats on si.SeatId equals seat.Id
            join row in db.Rows on seat.RowId equals row.Id
            join section in db.Sections on row.SectionId equals section.Id
            select new { si.SeatId, Section = section.Name, Row = row.Label, seat.Number, si.Status })
            .ToListAsync();

        // Map enum -> string after materializing; Status.ToString() may not translate on Npgsql.
        var seats = rows
            .Select(r => new SeatAvailabilityResponse(r.SeatId, r.Section, r.Row, r.Number, r.Status.ToString()))
            .ToList();
        return (SeatActionOutcome.Ok, seats);
    }

    public static Task<SeatActionOutcome> Hold(InventoryDbContext db, Guid performanceId, IReadOnlyList<Guid> seatIds) =>
        Transition(db, performanceId, seatIds, SeatStatus.Available, SeatStatus.Held);

    public static Task<SeatActionOutcome> Release(InventoryDbContext db, Guid performanceId, IReadOnlyList<Guid> seatIds) =>
        Transition(db, performanceId, seatIds, SeatStatus.Held, SeatStatus.Available);

    public static Task<SeatActionOutcome> Sell(InventoryDbContext db, Guid performanceId, IReadOnlyList<Guid> seatIds) =>
        Transition(db, performanceId, seatIds, SeatStatus.Held, SeatStatus.Sold);

    // ponytail: read-then-write with no concurrency token -> known double-book race. T13/T14 owns that.
    static async Task<SeatActionOutcome> Transition(
        InventoryDbContext db, Guid performanceId, IReadOnlyList<Guid> seatIds, SeatStatus from, SeatStatus to)
    {
        if (!await db.Performances.AnyAsync(p => p.Id == performanceId))
            return SeatActionOutcome.NotFound;

        var rows = await db.SeatInventories
            .Where(i => i.PerformanceId == performanceId && seatIds.Contains(i.SeatId))
            .ToListAsync();

        // All-or-nothing: every requested seat must exist and be in the source status, else change nothing.
        if (rows.Count != seatIds.Count || rows.Any(r => r.Status != from))
            return SeatActionOutcome.Conflict;

        foreach (var row in rows)
            row.Status = to;
        await db.SaveChangesAsync();
        return SeatActionOutcome.Ok;
    }
}
