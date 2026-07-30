# DeepBook Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **DeepBook connector** integrates StockSharp with the DeepBook liquidity protocol on Sui. It combines the public DeepBook indexer with a Sui full-node gRPC endpoint for pool data, wallet balances, and locally signed immediate swaps.

## Key capabilities

- Discover DeepBook pools and optionally restrict them by pool name, ID, or security code.
- Request Level 1 snapshots, order-book depth, and historical or polled tick trades.
- Download and poll time-frame candles from 1 minute through 7 days.
- Configure indexer, Sui full-node, package, clock-object, depth, history, and polling settings.
- Expose Sui token balances as a StockSharp portfolio when a wallet address is configured.
- Submit a market order as a locally signed DeepBook swap with configurable slippage protection.
- Track the resulting Sui transaction digest and swap execution.

## Typical use

Use this connector to monitor DeepBook pools, collect Sui DEX market data, or execute immediate swaps from a configured wallet. Public data does not require a private key; portfolio data requires a wallet address, and swap execution requires its Ed25519 signing key.

The transaction interface represents immediate swaps, not resting DeepBook orders. Limit, conditional, post-only, and time-in-force orders are not available, and an executed Sui transaction cannot be cancelled, replaced, or bulk-cancelled. Polling latency, indexer coverage, slippage, gas, liquidity, and Sui finality affect results.
