# CSV Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **CSV connector** connects StockSharp to a configurable data source. It translates provider-specific data and operations into the unified StockSharp message model, so applications can use the same subscriptions and workflows across different venues.

## Key capabilities

- Typical coverage: equities, futures, options.
- Instrument discovery and provider reference data.
- Market data supported by the adapter: Level 1 quotes, tick trades, order books and candles.
- Historical data requests for charting, analysis, and backtesting.
- Portfolio, balance, position, and execution-state updates.
- This adapter is intended for market data and does not route orders.
- Provider-specific transport, sessions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector to feed charts, market-data storage, analytics, research workflows, and strategy testing with provider data.

Available fields, instruments, and time ranges depend on the configured source data.
