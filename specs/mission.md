# Mission

**PortfolioManager** is a personal investment portfolio tracker for an individual investor managing **6 brokerage accounts** across Canadian and US markets.

The investor holds a mix of **stocks, call options, put options (written and bought), and cash** in both CAD and USD. Managing them across multiple platforms is painful — this app brings everything into one place.

## The Problem

Logging into 5 different brokerage portals to understand your overall financial position is slow, fragmented, and error-prone. There is no single view of total P&L, cash balances, or how each account is performing.

## What This App Does

- Tracks all 6 accounts, their cash balances (CAD and USD), and all open positions
- Fetches live (delayed) market prices automatically every 15 minutes
- Shows P&L per position, per account, and across the full portfolio
- Logs every buy, sell, option write, and cash update as a transaction
- Enforces account-type rules (e.g. put options can only be written in the Margin account)
- Shows a "Last updated X minutes ago" indicator so the investor always knows data freshness

## Accounts Being Tracked

| # | Account | Type | Owner |
|---|---------|------|-------|
| 1 | IBRK Margin | Margin | Primary |
| 2 | TFSA IBRK | TFSA | Primary |
| 3 | RRSP SHRF | RRSP | SHRF |
| 4 | RESP ALBRZ | RESP | ALBRZ |
| 5 | TFSA SAD | TFSA | SAD |
| 6 | RRSP SAD | RRSP | SAD |

## Asset Types Supported

- **Stocks** — Canadian (TSX) and US (NYSE/NASDAQ)
- **Call Options** — long (bought) in all accounts; short (written/covered calls) in all accounts
- **Put Options** — long (bought) in all accounts; short (written) **only in the Margin account**

## Options Convention

- 1 contract = 100 shares
- Quantity is entered as number of **contracts**
- P&L is based on **current market value** of the option

## What Success Looks Like

The investor opens the app, sees all 6 accounts on one dashboard — cash, positions, current prices, and total P&L — without logging into any brokerage. They can quickly log a new trade or cash update, and the prices refresh automatically in the background.
