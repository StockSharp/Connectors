# SimFin Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **SimFin connector** gives StockSharp read-only access to SimFin company fundamentals and daily price history. It maps provider records to StockSharp securities, Level 1 snapshots, daily candles, and a dedicated fundamental-data message type.

## Key capabilities

- Company and security discovery by ticker or SimFin company identifier.
- Latest available daily price record exposed as a Level 1 snapshot.
- Historical daily OHLCV candles; intraday intervals and live candle updates are not supported.
- Fundamental statements for configurable income-statement, balance-sheet, cash-flow, and derived datasets.
- Fiscal-period, date-range, standardized-versus-as-reported, ratio, and maximum-record controls.
- REST-only finite subscriptions for research and historical collection; there is no streaming transport.
- No tick trades, order books, news, portfolios, or trading operations are provided.
- SimFin authentication, throttling, and response formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector for fundamental screening, valuation research, daily-price analysis, and backtests that combine SimFin data with execution or intraday data from another connector.

Company coverage, statement fields, history, update frequency, rate limits, and access are controlled by SimFin and the connected API plan.
