# m.Stock Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **m.Stock connector** connects StockSharp to an Indian broker and its supported exchange segments. It translates provider-specific data and operations into the unified StockSharp message model, so applications can use the same subscriptions and workflows across different venues.

## Key capabilities

- Typical coverage: Indian equities, indices, futures, options, currency derivatives, funds and bonds.
- Instrument discovery and provider reference data.
- Market data supported by the adapter: Level 1 quotes, tick trades, order books and candles.
- Historical candle requests for charting, analysis, and backtesting.
- Provider-supported order submission, replacement, cancellation, and execution workflows.
- Portfolio, balance, position, order, and trade updates.
- Real-time subscriptions through the provider's streaming transport.
- Provider-specific transport, sessions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector for live strategies, trading terminals, order-management services, and monitoring tools that need direct access to an m.Stock account.

Available instruments, exchange segments, data depth, trading permissions, rate limits, and service availability are controlled by m.Stock, the exchanges, and the connected account.
