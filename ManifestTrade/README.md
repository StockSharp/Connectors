# Manifest Trade Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Manifest Trade connector** connects StockSharp to an on-chain trading and liquidity protocol. It translates provider-specific data and operations into the unified StockSharp message model, so applications can use the same subscriptions and workflows across different venues.

## Key capabilities

- Typical coverage: on-chain assets and liquidity pools.
- Instrument discovery and provider reference data.
- Market data supported by the adapter: Level 1 quotes, tick trades, order books and candles.
- Historical data requests for charting, analysis, and backtesting.
- Provider-supported swap or blockchain transaction submission.
- Portfolio, balance, position, and execution-state updates.
- Real-time subscriptions through the provider's streaming transport.
- Provider-specific transport, sessions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector for live strategies, trading terminals, order-management services, and monitoring tools that need direct access to the provider.

Available networks, pools, instruments, and transaction functions depend on Manifest Trade, the configured RPC or indexer services, and wallet permissions.
