using InventoryService.Data;
using InventoryService.SeatInventories;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Tests;

public class InventoryModelTests
{
    static InventoryDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql("Host=localhost") // no connection is opened just to read the model
            .Options;
        return new InventoryDbContext(options);
    }

    [Fact]
    public void SeatInventoryStatus_IsMappedAsString()
    {
        using var context = BuildContext();

        var status = context.Model
            .FindEntityType(typeof(SeatInventory))!
            .FindProperty(nameof(SeatInventory.Status))!;

        Assert.Equal(typeof(string), status.GetProviderClrType());
    }

    [Fact]
    public void SeatInventory_HasUniquePerformanceSeatIndex()
    {
        using var context = BuildContext();

        var index = context.Model
            .FindEntityType(typeof(SeatInventory))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(SeatInventory.PerformanceId), nameof(SeatInventory.SeatId) }));

        Assert.True(index.IsUnique);
    }
}
