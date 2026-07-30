# MarketData.app Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **MarketData.app connector** connects StockSharp to a professional market-data service. It translates provider-specific data into the unified StockSharp message model, so applications can use the same requests and workflows across different data sources.

## Key capabilities

- Typical coverage: equities, ETFs, options, indices and funds.
- Instrument discovery, including option-chain lookup, and provider reference data.
- Market data supported by the adapter: Level 1 quote snapshots and candles.
- Historical candle requests for charting, analysis, and backtesting; option candles are not provided by the service.
- This adapter is intended for market data and does not route orders.
- Provider-specific transport, sessions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector to feed charts, instrument and option discovery, market-data storage, analytics, research workflows, and strategy testing with provider data.

Available instruments, history depth, adjustments, rate limits, entitlements, and service availability are controlled by MarketData.app and by the connected API plan.
