# Velodrome Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Velodrome connector** connects StockSharp to Velodrome classic and Slipstream liquidity pools on Optimism. It maps configured pools, executable quotes, on-chain swaps, wallet balances, and submitted transactions to StockSharp messages.

## Key capabilities

- Instrument discovery for configured classic and concentrated-liquidity pools, including token metadata.
- Level 1 bid and ask quotes derived from executable pool probes, with WebSocket updates and polling fallback.
- Historical and live tick trades from on-chain swap logs, with time-frame candles built from those events.
- Immediate market swaps signed with an optional EVM private key, including allowance handling and configurable slippage.
- Wallet token balances plus transaction-receipt, order-state, and execution updates.
- Historical collection is bounded by configured Optimism block ranges and block counts.
- No centralized order book, resting limit orders, atomic replacement, or cancellation is available.
- Optimism RPC calls, token units, pool variants, signing, and event logs are hidden behind the standard StockSharp API.

## Typical use

Use this connector for Optimism DEX monitoring, Velodrome pool analytics, event-based backtests, wallet tracking, and direct swap execution.

Pool coverage, executable prices, liquidity, RPC history, gas costs, transaction finality, and endpoint availability depend on Velodrome, Optimism, and the configured RPC services.
