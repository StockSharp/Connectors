# Coincall Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Coincall connector** integrates StockSharp with Coincall options and futures. A product setting selects the derivatives surface, while REST provides snapshots and history and authenticated WebSocket sessions provide live market and private updates.

## Key capabilities

- Discover Coincall options or futures instruments.
- Subscribe to Level 1 quotes, market depth, tick trades, and time-frame candles.
- Download recent trades and historical candles before continuing with live WebSocket updates.
- Submit limit, market, and trigger-price conditional orders with supported GTC, IOC, FOK, post-only, and reduce-only parameters.
- Modify or cancel individual orders and cancel groups of matching orders.
- Load balances, positions, open and historical orders, and own trades.
- Reconcile private state at a configurable interval.

## Typical use

Use this connector for derivatives monitoring and automated options or futures trading on Coincall. REST instrument discovery and snapshots can connect without credentials, but WebSocket streaming and every private operation require an API key and secret.

Only one product surface is selected per adapter instance. Iceberg orders and absolute-expiry orders are not supported, order books are snapshot based, and no order-log feed is exposed. Available instruments, trading permissions, and API limits are controlled by Coincall.
