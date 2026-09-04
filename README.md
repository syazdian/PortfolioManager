# Portfolio Manager

A personal investment portfolio tracker built with **Blazor Server + .NET 10 + SQLite**.

Tracks multiple brokerage accounts (stocks, options), fetches delayed market prices automatically from Yahoo Finance, and shows unrealized P&L across all positions on a single dashboard.

## Features

- 📊 Dashboard with all accounts — cash (CAD & USD), open positions, total P&L
- 💹 Per-account detail with inline position editing
- 🔁 Auto price refresh every 15 minutes via Yahoo Finance (no API key required)
- 📝 Trade log: Buy, Sell, Write Option, Close Option, Dividends, Cash updates
- 📐 Spread grouping — group related option legs (e.g. Bull Put Spread) into a collapsible parent row
- ⚙️ Settings page to add/edit/delete accounts at runtime (no hardcoding)
- 🛡️ Account-type rule enforcement (e.g. short puts only in Margin accounts)
- 🌑 Dark theme

## Tech Stack

| Layer | Technology |
|---|---|
| UI | Blazor Server (InteractiveServer) |
| Backend | ASP.NET Core / .NET 10 |
| Database | SQLite via EF Core 10 (`EnsureCreated`) |
| Price Feed | Yahoo Finance (crumb + cookie auth) |
| Background | `IHostedService` refresh every 15 min |

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows, macOS, or Linux

### Run

```bash
git clone https://github.com/syazdian/PortfolioManager.git
cd PortfolioManager
dotnet run --project src/PortfolioManager.Web
```

The app creates the SQLite database automatically at `C:\Data\portfolio.db` on first run (path is configurable in `Program.cs`).

Open your browser at `http://localhost:5030`.

### First Use

1. Go to **⚙ Settings** → add your accounts (name, type, owner)
2. Go to **➕ Log Trade** → log your positions
3. The dashboard will auto-populate with live prices within the first refresh cycle

## Project Structure

```
src/
  PortfolioManager.Core/     # Models, DbContext, PortfolioService
  PortfolioManager.Prices/   # Yahoo Finance client, background refresh, PriceState
  PortfolioManager.Web/      # Blazor Server UI
specs/                       # Design notes and roadmap
```

## Notes

- Prices are **delayed quotes** from Yahoo Finance — not real-time
- The SQLite database is local-only and excluded from git (`.gitignore`)
- No API keys or credentials are required

## License

MIT
