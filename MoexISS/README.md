# MOEX ISS Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **MOEX ISS connector** connects StockSharp to a source of Russian exchange and market data. It translates provider-specific data and operations into the unified StockSharp message model, so applications can use the same subscriptions and workflows across different venues.

## Key capabilities

- Typical coverage: digital assets, equities, futures, options, FX and CFDs, bonds and fixed income, funds and ETFs, commodities, indices.
- Instrument discovery and provider reference data.
- Market data supported by the adapter: Level 1 quotes, tick trades, order books, candles and order-log events.
- Historical data requests for charting, analysis, and backtesting.
- This adapter is intended for market data and does not route orders.
- Provider-specific transport, sessions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector to feed charts, market-data storage, analytics, research workflows, and strategy testing with provider data.

Available instruments, data depth, trading permissions, rate limits, and service availability are controlled by MOEX ISS and by the connected account or API plan.
