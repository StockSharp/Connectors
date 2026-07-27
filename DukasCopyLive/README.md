# Dukascopy Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Dukascopy connector** connects StockSharp to an FX/CFD broker or trading platform. It translates provider-specific data and operations into the unified StockSharp message model, so applications can use the same subscriptions and workflows across different venues.

## Key capabilities

- Typical coverage: equities, FX and CFDs, bonds and fixed income, commodities, indices.
- Instrument discovery and provider reference data.
- Market data supported by the adapter: Level 1 quotes, tick trades, order books and candles.
- Historical data requests for charting, analysis, and backtesting.
- Provider-supported order submission and execution workflows.
- Portfolio, balance, position, and execution-state updates.
- Provider-specific transport, sessions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector for live strategies, trading terminals, order-management services, and monitoring tools that need direct access to the provider.

Available instruments, data depth, trading permissions, rate limits, and service availability are controlled by Dukascopy and by the connected account or API plan.
