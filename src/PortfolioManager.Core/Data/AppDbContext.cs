using Microsoft.EntityFrameworkCore;
using PortfolioManager.Core.Models;

namespace PortfolioManager.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PriceCache> PriceCache => Set<PriceCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PriceCache>()
            .HasKey(p => p.Symbol);
    }
}
