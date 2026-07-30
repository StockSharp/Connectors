# CoinCatch Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **CoinCatch connector** integrates StockSharp with CoinCatch spot and derivatives markets. A product setting selects spot, USDT-margined futures, or coin-margined futures, while REST and WebSocket APIs provide market data and authenticated trading.

## Key capabilities

- Discover instruments for the selected CoinCatch product.
- Subscribe to Level 1 quotes, market depth, tick trades, and time-frame candles.
- Download historical candles and continue with live WebSocket updates.
- Submit limit and market orders, including futures reduce-only and limit post-only parameters.
- Cancel individual orders or all orders for a symbol.
- Load balances, positions, open and historical orders, and own trades.
- Reconcile private state with API key, secret, and passphrase authentication.

## Typical use

Use this connector for spot or futures market monitoring, candle history, and automated trading on CoinCatch. Select the product before connecting, and provide credentials with the appropriate read or trade permissions for private operations.

The adapter does not expose CoinCatch plan/trigger orders, iceberg orders, or atomic order replacement. Order books are snapshot based and no order-log stream is available. Instrument rules, account mode, permissions, and exchange rate limits must be respected.
