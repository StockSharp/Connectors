# Buda Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Buda connector** integrates StockSharp with the Buda.com spot cryptocurrency exchange. Public market data is available without credentials, while authenticated REST operations use an API key and secret.

## Key capabilities

- Discover the spot instruments exposed by Buda.
- Subscribe to Level 1 quotes, market depth, and tick trades.
- Combine public WebSocket updates with REST snapshots and reconciliation.
- Submit limit and market orders and cancel individual or grouped orders.
- Load balances, portfolio state, active and historical orders, and own trades.
- Reconcile private state at a configurable polling interval.

## Typical use

Use this connector for live Buda spot-market monitoring and authenticated trading through StockSharp. Public-data applications can connect without credentials; order and account workflows require a Buda API key and secret with the necessary permissions.

The adapter does not provide candles or an order-log feed, and order books are delivered as snapshots rather than incremental updates. Atomic order replacement is not supported, so a strategy must cancel and submit a new order separately. Exchange permissions and rate limits still apply.
