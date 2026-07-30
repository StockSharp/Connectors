# Pendle Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Pendle connector** connects StockSharp to an on-chain yield-trading protocol. It translates protocol data and wallet operations into the unified StockSharp message model, so applications can use standard subscriptions and transaction workflows for Pendle markets.

## Key capabilities

- Typical coverage: on-chain yield-bearing assets, principal tokens, yield tokens, and Pendle markets.
- Instrument discovery and protocol reference data.
- Market data supported by the adapter: Level 1 quotes and candles.
- Historical candle requests and continuing market-data updates for charting, analysis, and strategy workflows.
- Provider-supported token conversion and blockchain transaction submission, including required token approvals.
- Wallet portfolio, balance, position, and execution-state updates.
- Protocol-specific HTTP and RPC transport, wallet transactions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector for yield-market monitoring, live strategies, wallet-aware trading tools, and services that need to quote or execute conversions through Pendle.

Available networks, markets, tokens, quotes, transaction functions, fees, and service availability depend on Pendle, the configured API and RPC endpoints, current chain conditions, and wallet permissions.
