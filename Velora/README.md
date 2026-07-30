# Velora Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Velora connector** connects StockSharp to the Velora Market API and supported EVM networks. It represents configured token pairs as instruments and maps executable prices, wallet balances, and routed swaps to StockSharp messages.

## Key capabilities

- Configured token-pair discovery on Ethereum, Optimism, BNB Chain, Gnosis, Polygon, Base, Arbitrum, and Avalanche.
- Polling-based Level 1 bid and ask prices derived from executable Velora route quotes.
- Immediate market-swap construction, signing, and broadcast through the selected chain's JSON-RPC endpoint.
- Optional automatic token approval, configurable slippage, quote probe volume, and receipt timeout.
- Wallet token balances plus tracked transaction-receipt, order-state, and execution updates.
- A Velora partner identifier, wallet address, token pairs, API endpoint, and RPC endpoint are configurable.
- No tick trades, order books, candles, historical market data, resting orders, replacement, or cancellation is available.
- Velora routes, token units, approvals, signing, and EVM receipts are hidden behind the standard StockSharp API.

## Typical use

Use this connector for cross-token quote monitoring, wallet dashboards, and direct Velora-routed swap execution on a supported EVM network.

Pair coverage, route availability, liquidity, price impact, gas costs, approvals, transaction finality, and service limits depend on Velora, the selected network, and the configured RPC provider.
