# KyberSwap Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **KyberSwap connector** connects StockSharp to KyberSwap Aggregator API v1 and EVM networks. It exposes configured token pairs as StockSharp instruments, derives executable quotes from aggregator routes, and submits signed on-chain swaps.

## Key capabilities

- Configured token-pair discovery with token metadata on Ethereum, Optimism, BNB Chain, Polygon, Base, Arbitrum, Avalanche, and Linea.
- Level 1 bid and ask quotes calculated from executable aggregator routes for a configurable probe volume.
- Periodic REST polling for active Level 1 subscriptions; historical quote events and streaming transport are not available.
- Immediate market swaps signed locally and broadcast through EVM JSON-RPC, with configurable slippage and automatic token approval.
- Wallet token balances and portfolio updates through chain calls.
- Tracking of connector-submitted swaps by transaction hash until an EVM receipt confirms success or failure.
- No tick trades, order books, candles, limit orders, or replacement and cancellation of already broadcast transactions.

## Typical use

Use this connector for route-aware DEX quote monitoring and automated market swaps on the supported EVM networks.

Quotes can be queried without trading credentials, but execution requires a wallet and private key plus a working RPC endpoint. Token definitions, route liquidity, allowances, gas costs, slippage, receipt latency, API limits, and network availability affect the result of every swap.
