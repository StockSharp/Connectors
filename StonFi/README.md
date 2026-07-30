# STON.fi Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **STON.fi connector** connects StockSharp to STON.fi liquidity pools and the TON blockchain. It represents configured or discovered pools as instruments and translates swap quotes, pool events, wallet balances, and submitted swaps into StockSharp messages.

## Key capabilities

- Discovery of configured pools or a limited set of popular STON.fi pools, with token metadata.
- Polling-based Level 1 bid and ask quotes derived from executable swap simulations.
- Historical and live tick trades from TON pool events, with time-frame candles built from those swaps.
- Immediate market-swap submission using a TON Wallet V4 mnemonic, configurable slippage, and TON Center broadcast.
- Wallet token balances plus tracked swap order and execution-status updates.
- Historical requests are bounded by the configured TON block range; live delivery relies on polling.
- No centralized order book, resting limit orders, order replacement, or cancellation is available.
- STON.fi REST data, TON units, wallet signing, and blockchain events are hidden behind the standard StockSharp API.

## Typical use

Use this connector for TON DEX quote monitoring, pool analytics, swap-based strategies, wallet tracking, and direct STON.fi market execution.

Pool coverage, quote quality, event history, routing, fees, transaction finality, and service availability depend on STON.fi, TON Center, the configured endpoints, and blockchain state.
