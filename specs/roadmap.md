# Roadmap

## Phase 1 - Foundation (Current)
- [ ] Scaffold solution: `PortfolioManager.Core`, `PortfolioManager.Prices`, `PortfolioManager.Web`
- [ ] Define EF Core models: `Account`, `Position`, `Transaction`, `PriceCache`
- [ ] Set up SQLite `AppDbContext` with EF Core migrations
- [ ] Seed the 6 accounts (IBRK Margin, TFSA IBRK, RRSP SHRF, RESP ALBRZ, TFSA SAD, RRSP SAD)
- [ ] Build `PortfolioService` - add/update positions, log transactions, compute P&L

## Phase 2 - Price Feed
- [ ] Implement Yahoo Finance HTTP client in `PortfolioManager.Prices`
- [ ] Support TSX symbols (e.g. `XIU.TO`) and US symbols (e.g. `AAPL`)
- [ ] Support option contract price lookup
- [ ] `IHostedService` background timer - refresh all symbols every 15 minutes
- [ ] Persist prices to `PriceCache` table with `FetchedAt` timestamp
- [ ] Shared `PriceState` service exposes "Last updated X min ago" to Blazor components

## Phase 3 - Dashboard UI (Blazor Server)
- [ ] `Dashboard.razor` - account cards showing cash (CAD/USD), position value, total P&L
- [ ] `AccountDetail.razor` - position rows: Symbol | Qty | Avg Cost | Price | Day Change | P&L $ | P&L %
- [ ] `Transactions.razor` - form to log Buy / Sell / Write Option / Cash Update
- [ ] Options flagged with type (Call/Put) and direction (Long/Short)
- [ ] Warning badge if a position violates account-type rules (e.g. short put in TFSA)

## Phase 4 - Polish
- [ ] CAD and USD totals shown separately + combined "in CAD" total using fetched FX rate
- [ ] Daily / monthly P&L toggle on Dashboard
- [ ] Export to CSV
- [ ] Dark mode

## Phase 5 - Android (Future - architecture TBD)
> NOTE: SQLite is local to Windows. Android access requires a different data strategy.
> Options to evaluate when the time comes: REST API wrapper, cloud sync, or full rewrite with shared Core logic.
- [ ] Decide on Android data access strategy
- [ ] Reuse `PortfolioManager.Core` business logic where possible
- [ ] Build mobile UI layer

## Out of Scope (for now)
- Android / mobile (deferred to Phase 5)
- Cloud sync or multi-device support
- Real-time (non-delayed) price data
- Brokerage API integration (automatic trade import)
- Tax reporting
