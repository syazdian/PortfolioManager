using Microsoft.EntityFrameworkCore;
using PortfolioManager.Core.Data;
using PortfolioManager.Core.Services;
using PortfolioManager.Prices;
using PortfolioManager.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor ───────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Database ─────────────────────────────────────────────────────────────────
var dbPath = Path.Combine("C:\\Data", "portfolio.db");
Directory.CreateDirectory("C:\\Data");

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// ── Business Logic ────────────────────────────────────────────────────────────
builder.Services.AddScoped<PortfolioService>();

// ── Price Feed ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<PriceState>();
builder.Services.AddSingleton<YahooFinanceClient>();
builder.Services.AddSingleton<PriceRefreshService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PriceRefreshService>());

var app = builder.Build();

// ── Migrate & seed on startup ─────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();

    // Safe schema migration: add SpreadGroupName column if it doesn't exist yet
    try
    {
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE Positions ADD COLUMN SpreadGroupName TEXT NULL");
    }
    catch { /* column already exists */ }
}

// ── Pipeline ──────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

