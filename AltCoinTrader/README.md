# AltCoinTrader Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **AltCoinTrader connector** integrates StockSharp with the South African AltCoinTrader spot market. Its ZAR-denominated order books make it useful for local price discovery, market monitoring, data collection, and automated crypto trading.

## Key capabilities

- Spot instrument discovery with trading state, price and quantity precision, and minimum order value.
- Level 1 quotes, Level 2 order books, and public trades.
- Real-time ticker, depth, and trade subscriptions through the public WebSocket.
- REST snapshots and recent public trades.
- Balances, open and historical orders, private fills, and live account updates through the authenticated WebSocket.
- Limit orders with GTC, IOC, and FOK policies, market orders, individual cancellation, and filtered bulk cancellation.
- Configurable REST and WebSocket service addresses.

Public market data is available without credentials. Portfolio and trading operations require an AltCoinTrader API key and secret with the appropriate account permissions.
