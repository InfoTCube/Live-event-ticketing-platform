using InventoryService.Events;
using InventoryService.Performances;
using InventoryService.Rows;
using InventoryService.Seats;
using InventoryService.SeatInventories;
using InventoryService.Sections;
using InventoryService.Venues;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Data;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Row> Rows => Set<Row>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Performance> Performances => Set<Performance>();
    public DbSet<SeatInventory> SeatInventories => Set<SeatInventory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Section>()
            .HasOne<Venue>().WithMany().HasForeignKey(s => s.VenueId);

        modelBuilder.Entity<Row>()
            .HasOne<Section>().WithMany().HasForeignKey(r => r.SectionId);

        modelBuilder.Entity<Seat>(seat =>
        {
            seat.HasOne<Row>().WithMany().HasForeignKey(s => s.RowId);
            seat.HasIndex(s => new { s.RowId, s.Number }).IsUnique();
        });

        modelBuilder.Entity<Performance>(performance =>
        {
            performance.HasOne<Event>().WithMany().HasForeignKey(p => p.EventId);
            performance.HasOne<Venue>().WithMany().HasForeignKey(p => p.VenueId);
        });

        modelBuilder.Entity<SeatInventory>(inventory =>
        {
            inventory.HasOne<Performance>().WithMany().HasForeignKey(i => i.PerformanceId);
            inventory.HasOne<Seat>().WithMany().HasForeignKey(i => i.SeatId);
            inventory.Property(i => i.Status).HasConversion<string>();
            inventory.HasIndex(i => new { i.PerformanceId, i.SeatId }).IsUnique();
        });
    }
}
