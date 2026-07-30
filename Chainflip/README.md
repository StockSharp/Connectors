# Chainflip Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Chainflip connector** integrates StockSharp with the Chainflip cross-chain liquidity network. It combines public State Chain and swap-service data with optional wallet configuration for submitting cross-chain swaps through the StockSharp transaction model.

## Key capabilities

- Discover supported Chainflip pools and assets.
- Receive Level 1 values, pool depth, and trades derived from pool state and fills.
- Use configurable State Chain, quote-service, Ethereum, and Arbitrum endpoints.
- Request a quote and submit a market order as a protected cross-chain swap.
- Track submitted swaps and expose wallet balances through portfolio messages.
- Configure destination addresses for assets on the supported chains.

## Typical use

Use this connector to monitor Chainflip liquidity or to execute immediate cross-chain swaps from a configured wallet. Public market-data workflows do not require a signing key; execution requires the wallet address, private key, destination addresses, and working chain endpoints.

This is a protocol integration rather than a centralized-exchange order interface. The adapter does not provide candles, limit orders, conditional orders, or resting orders. Once a swap transaction is broadcast it cannot be cancelled, replaced, or bulk-cancelled. Network fees, finality, liquidity, slippage, and chain availability affect execution.
