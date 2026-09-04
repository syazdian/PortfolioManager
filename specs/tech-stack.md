# Tech Stack

PortfolioManager is a **Blazor Server** web application that runs locally on Windows (`localhost`). You launch it like a desktop app and open it in your browser — no cloud, no deployment, no server required. All data is stored in a local SQLite file.

> **Android / mobile is explicitly out of scope for now.** The architecture will be revisited when that phase begins.

## Core

| Layer | Technology |
|---|---|
| UI Framework | Blazor Server (ASP.NET Core) |
| Language | C# 13 / .NET 10 |
| Database | SQLite (local file, stored in AppData) |
| ORM | Entity Framework Core 10 + EF Core SQLite provider |
| Migrations | EF Core Migrations |
| Price Data | Yahoo Finance (unofficial, ~15 min delayed) via direct HTTP + `System.Text.Json` |
| Background Jobs | `IHostedService` (built-in ASP.NET Core) for price refresh timer |
| Charting | (future) — to be selected when needed |
| IDE | Visual Studio 2026 (Community) |

## Project Structure

```
PortfolioManager/
├── PortfolioManager.sln
├── PortfolioManager.Core/          ← Models, DbContext, business logic, validation
│   ├── Models/                     ← Account, Position, Transaction, PriceCache
│   ├── Data/                       ← AppDbContext, EF migrations
│   └── Services/                   ← PortfolioService, P&L calculations
├── PortfolioManager.Prices/        ← Yahoo Finance HTTP client, price refresh hosted service
└── PortfolioManager.Web/           ← Blazor Server app (UI only — pages, components, DI wiring)
    ├── Pages/
    │   ├── Dashboard.razor         ← All accounts at a glance
    │   ├── AccountDetail.razor     ← Single account deep-dive
    │   └── Transactions.razor      ← Log buys / sells / cash updates
    └── Components/                 ← Reusable UI components (position row, account card, etc.)
```

## Key Design Decisions

- **Blazor Server on localhost**: runs as a normal .NET process, UI is in the browser, SQLite is on disk — simple and zero-friction
- **SQLite local file**: stored in the user's AppData folder; portable, no installation, no server
- **EF Core 10**: typed models, migrations for schema changes, LINQ queries
- **Price refresh**: `IHostedService` background timer fires every 15 minutes; UI shows "Last updated X min ago" via a shared `PriceState` service
- **Currency**: CAD and USD tracked separately on every account and position; no forced conversion on input
- **Options**: tracked as contracts (1 contract = 100 shares); P&L based on current market value

## Account-Type Rules (enforced in Core)

| Account Type | Buy Calls | Write Covered Calls | Write Puts |
|---|---|---|---|
| Margin | ✅ | ✅ | ✅ |
| TFSA | ✅ | ✅ | ❌ |
| RRSP | ✅ | ✅ | ❌ |
| RESP | ✅ | ✅ | ❌ |