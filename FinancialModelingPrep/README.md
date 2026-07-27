# Financial Modeling Prep Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Financial Modeling Prep connector** connects StockSharp to a professional market-data and analytics service. It translates provider-specific data and operations into the unified StockSharp message model, so applications can use the same subscriptions and workflows across different venues.

## Key capabilities

- Typical coverage: digital assets, equities, options, FX and CFDs, funds and ETFs, commodities, indices.
- Instrument discovery and provider reference data.
- Market data supported by the adapter: Level 1 quotes, tick trades, candles and financial news.
- Historical data requests for charting, analysis, and backtesting.
- Real-time subscriptions through the provider's streaming transport.
- This adapter is intended for market data and does not route orders.
- Provider-specific transport, sessions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector to feed charts, market-data storage, analytics, research workflows, and strategy testing with provider data.

Available instruments, data depth, trading permissions, rate limits, and service availability are controlled by Financial Modeling Prep and by the connected account or API plan.
