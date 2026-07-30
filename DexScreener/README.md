# DexScreener Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **DexScreener connector** brings multi-chain decentralized-exchange pair analytics into StockSharp through DexScreener's public REST API. It is a read-only market-data adapter and does not require API credentials.

## Key capabilities

- Pair discovery by chain identifier, token address, exact pair, or free-text search, with StockSharp skip and count limits.
- Level 1 snapshots with the latest USD and native-token prices, 24-hour volume and price change, liquidity, and trading state.
- Periodic REST refresh for active Level 1 subscriptions; the polling interval is configurable and defaults to 30 seconds.
- Coverage across the chains and liquidity pools indexed by DexScreener.
- Public access without an API key or private account session.
- No historical Level 1 events or real-time streaming transport.
- No tick trades, order books, candles, order entry, portfolio data, or account operations.

## Typical use

Use this connector for DEX-pair discovery, watchlists, liquidity screens, and dashboards that need periodically refreshed aggregate market metrics.

It is not an execution connector or a source for backtesting-grade event history. Pair coverage, field availability, freshness, and request limits are determined by DexScreener and the underlying decentralized venues.
