# 0x Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **0x connector** connects StockSharp to 0x Swap API v2 and supported EVM networks. It represents configured token pairs as instruments and maps executable prices, wallet balances, and routed swaps to StockSharp messages.

## Key capabilities

- Configured token-pair discovery on Ethereum, Optimism, BNB Chain, Polygon, Base, Arbitrum, Avalanche, and Linea.
- Polling-based Level 1 bid and ask prices derived from executable 0x price probes.
- Immediate market-swap quote retrieval, signing, and broadcast through the selected chain's JSON-RPC endpoint.
- Optional automatic allowance approval, configurable slippage, quote probe volume, and receipt timeout.
- Wallet token balances plus tracked transaction-receipt, order-state, and execution updates.
- A 0x Dashboard API key, wallet address, token pairs, API endpoint, and RPC endpoint are configurable.
- No tick trades, order books, candles, historical market data, resting orders, replacement, or cancellation is available.
- 0x routes, token units, approvals, signing, and EVM receipts are hidden behind the standard StockSharp API.

## Typical use

Use this connector for executable token-price monitoring, wallet dashboards, and direct 0x-routed swap execution on a supported EVM network.

Pair coverage, route availability, liquidity, price impact, gas costs, approvals, transaction finality, and service limits depend on 0x, the selected network, and the configured RPC provider.
