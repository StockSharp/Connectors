# Dexalot Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Dexalot connector** connects StockSharp to Dexalot's on-chain central limit order book on the Avalanche-based Dexalot L1. It combines public REST and WebSocket data with EVM contract calls for spot trading and account state.

## Key capabilities

- Discovery and reference data for Dexalot spot token pairs.
- Level 1 and order-book snapshots from contract reads, followed by live WebSocket updates; historical Level 1 and book events are not available.
- WebSocket trade and candle feeds, with date and count filtering over the history supplied by the provider and continued live delivery.
- Time-frame candles for 5, 15, and 30 minutes; 1 and 4 hours; and 1 day.
- On-chain limit and market orders, including post-only behavior and configurable self-trade prevention.
- Order replacement, individual cancellation, and bulk cancellation; iceberg orders, absolute expiry, and bulk position closing are not supported.
- Portfolio token balances, order and fill history, and private-state reconciliation through REST, WebSocket, and EVM RPC.

## Typical use

Use this connector for live spot strategies, terminals, and order-management services that need access to Dexalot's order book and on-chain execution.

Trading requires a wallet address and private key and incurs network gas and confirmation latency. Available pairs, stream backfill, API limits, contract availability, and finality depend on Dexalot and the selected network endpoints.
