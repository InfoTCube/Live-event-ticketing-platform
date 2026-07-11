using InventoryService.Data;
using InventoryService.SeatInventories;
using InventoryService.Seed;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Tests;

public class SeatInventoryOperationsTests
{
    static InventoryDbContext Seeded()
    {
        var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        InventorySeeder.Seed(db);
        return db;
    }

    static (Guid PerformanceId, List<Guid> SeatIds) Pick(InventoryDbContext db, int count)
    {
        var performanceId = db.Performances.First().Id;
        var seatIds = db.SeatInventories
            .Where(i => i.PerformanceId == performanceId)
            .Select(i => i.SeatId)
            .Take(count)
            .ToList();
        return (performanceId, seatIds);
    }

    static SeatStatus StatusOf(InventoryDbContext db, Guid performanceId, Guid seatId) =>
        db.SeatInventories.Single(i => i.PerformanceId == performanceId && i.SeatId == seatId).Status;

    [Fact]
    public async Task Hold_FlipsAvailableToHeld()
    {
        using var db = Seeded();
        var (performanceId, seatIds) = Pick(db, 2);

        var outcome = await SeatInventoryOperations.Hold(db, performanceId, seatIds);

        Assert.Equal(SeatActionOutcome.Ok, outcome);
        Assert.All(seatIds, id => Assert.Equal(SeatStatus.Held, StatusOf(db, performanceId, id)));
    }

    [Fact]
    public async Task Release_FlipsHeldBackToAvailable()
    {
        using var db = Seeded();
        var (performanceId, seatIds) = Pick(db, 2);
        await SeatInventoryOperations.Hold(db, performanceId, seatIds);

        var outcome = await SeatInventoryOperations.Release(db, performanceId, seatIds);

        Assert.Equal(SeatActionOutcome.Ok, outcome);
        Assert.All(seatIds, id => Assert.Equal(SeatStatus.Available, StatusOf(db, performanceId, id)));
    }

    [Fact]
    public async Task Sell_FlipsHeldToSold()
    {
        using var db = Seeded();
        var (performanceId, seatIds) = Pick(db, 2);
        await SeatInventoryOperations.Hold(db, performanceId, seatIds);

        var outcome = await SeatInventoryOperations.Sell(db, performanceId, seatIds);

        Assert.Equal(SeatActionOutcome.Ok, outcome);
        Assert.All(seatIds, id => Assert.Equal(SeatStatus.Sold, StatusOf(db, performanceId, id)));
    }

    [Fact]
    public async Task Sell_OnUnheldSeat_Conflicts()
    {
        using var db = Seeded();
        var (performanceId, seatIds) = Pick(db, 1);

        var outcome = await SeatInventoryOperations.Sell(db, performanceId, seatIds);

        Assert.Equal(SeatActionOutcome.Conflict, outcome);
        Assert.Equal(SeatStatus.Available, StatusOf(db, performanceId, seatIds[0]));
    }

    [Fact]
    public async Task Hold_IsAllOrNothing_WhenBatchContainsHeldSeat()
    {
        using var db = Seeded();
        var (performanceId, seatIds) = Pick(db, 3);
        await SeatInventoryOperations.Hold(db, performanceId, new[] { seatIds[0] });

        var outcome = await SeatInventoryOperations.Hold(db, performanceId, seatIds);

        Assert.Equal(SeatActionOutcome.Conflict, outcome);
        Assert.Equal(SeatStatus.Held, StatusOf(db, performanceId, seatIds[0]));      // unchanged
        Assert.Equal(SeatStatus.Available, StatusOf(db, performanceId, seatIds[1])); // untouched
        Assert.Equal(SeatStatus.Available, StatusOf(db, performanceId, seatIds[2]));
    }

    [Fact]
    public async Task Hold_UnknownPerformance_NotFound()
    {
        using var db = Seeded();
        var (_, seatIds) = Pick(db, 1);

        var outcome = await SeatInventoryOperations.Hold(db, Guid.NewGuid(), seatIds);

        Assert.Equal(SeatActionOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task GetAvailability_ReturnsLabeledSeatsWithStatus()
    {
        using var db = Seeded();
        var (performanceId, seatIds) = Pick(db, 1);
        await SeatInventoryOperations.Hold(db, performanceId, seatIds);

        var (outcome, seats) = await SeatInventoryOperations.GetAvailability(db, performanceId);

        Assert.Equal(SeatActionOutcome.Ok, outcome);
        Assert.Equal(100, seats.Count);
        Assert.Contains(seats, s => s.SeatId == seatIds[0] && s.Status == "Held");
        var seat = seats.First(s => s.SeatId == seatIds[0]);
        Assert.False(string.IsNullOrEmpty(seat.Section));
        Assert.False(string.IsNullOrEmpty(seat.Row));
    }

    [Fact]
    public async Task GetAvailability_UnknownPerformance_NotFound()
    {
        using var db = Seeded();

        var (outcome, seats) = await SeatInventoryOperations.GetAvailability(db, Guid.NewGuid());

        Assert.Equal(SeatActionOutcome.NotFound, outcome);
        Assert.Empty(seats);
    }
}
