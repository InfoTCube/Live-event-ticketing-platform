using InventoryService.Data;
using InventoryService.SeatInventories;
using InventoryService.Seed;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Tests;

public class InventorySeederTests
{
    static InventoryDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public void Seed_InsertsFullSeatMap()
    {
        using var db = BuildContext();

        InventorySeeder.Seed(db);

        Assert.Equal(1, db.Events.Count());
        Assert.Equal(1, db.Venues.Count());
        Assert.Equal(2, db.Sections.Count());
        Assert.Equal(10, db.Rows.Count());
        Assert.Equal(100, db.Seats.Count());
        Assert.Equal(3, db.Performances.Count());
        Assert.Equal(300, db.SeatInventories.Count());
        Assert.True(db.SeatInventories.All(i => i.Status == SeatStatus.Available));
    }

    [Fact]
    public void Seed_IsIdempotent()
    {
        using var db = BuildContext();

        InventorySeeder.Seed(db);
        InventorySeeder.Seed(db);

        Assert.Equal(1, db.Venues.Count());
        Assert.Equal(100, db.Seats.Count());
        Assert.Equal(300, db.SeatInventories.Count());
    }
}
