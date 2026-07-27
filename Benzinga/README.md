# Benzinga Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Benzinga connector** connects StockSharp to a financial news and event-data service. It translates provider-specific data and operations into the unified StockSharp message model, so applications can use the same subscriptions and workflows across different venues.

## Key capabilities

- Typical coverage: equities, futures, options, funds and ETFs, indices.
- Instrument discovery and provider reference data.
- Market data supported by the adapter: Level 1 quotes, candles and financial news.
- Historical data requests for charting, analysis, and backtesting.
- Real-time subscriptions through the provider's streaming transport.
- This adapter is intended for market data and does not route orders.
- Provider-specific transport, sessions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector to bring provider news and event streams into monitoring, analytics, alerting, and event-driven strategies.

Available instruments, data depth, trading permissions, rate limits, and service availability are controlled by Benzinga and by the connected account or API plan.
