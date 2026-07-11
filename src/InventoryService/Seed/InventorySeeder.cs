using InventoryService.Data;
using InventoryService.Events;
using InventoryService.Performances;
using InventoryService.Rows;
using InventoryService.Seats;
using InventoryService.SeatInventories;
using InventoryService.Sections;
using InventoryService.Venues;

namespace InventoryService.Seed;

public static class InventorySeeder
{
    public static void Seed(InventoryDbContext db)
    {
        if (db.Venues.Any()) return;

        var @event = new Event { Id = Guid.NewGuid(), Name = "Aurora Live" };
        var venue = new Venue { Id = Guid.NewGuid(), Name = "Grand Arena" };

        var seats = new List<Seat>();
        foreach (var sectionName in new[] { "Floor", "Balcony" })
        {
            var section = new Section { Id = Guid.NewGuid(), VenueId = venue.Id, Name = sectionName };
            db.Add(section);

            foreach (var label in new[] { "A", "B", "C", "D", "E" })
            {
                var row = new Row { Id = Guid.NewGuid(), SectionId = section.Id, Label = label };
                db.Add(row);

                for (var number = 1; number <= 10; number++)
                {
                    var seat = new Seat { Id = Guid.NewGuid(), RowId = row.Id, Number = number };
                    seats.Add(seat);
                    db.Add(seat);
                }
            }
        }

        var start = new DateTime(2026, 8, 1, 19, 0, 0, DateTimeKind.Utc);
        var performances = Enumerable.Range(0, 3)
            .Select(i => new Performance
            {
                Id = Guid.NewGuid(),
                EventId = @event.Id,
                VenueId = venue.Id,
                StartsAtUtc = start.AddDays(i * 7),
            })
            .ToList();

        db.Add(@event);
        db.Add(venue);
        db.AddRange(performances);

        foreach (var performance in performances)
            foreach (var seat in seats)
                db.Add(new SeatInventory
                {
                    Id = Guid.NewGuid(),
                    PerformanceId = performance.Id,
                    SeatId = seat.Id,
                    Status = SeatStatus.Available,
                });

        db.SaveChanges();
    }
}
