# Coinstore Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Coinstore connector** connects StockSharp to Coinstore's spot cryptocurrency market. It is useful for tracking the exchange's broad listing market and for automating trading in crypto-to-stablecoin pairs.

## Key capabilities

- Spot instrument discovery with trading state, price and amount precision, and minimum-order metadata.
- Level 1 values, Level 2 order books, public trades, and OHLCV candles.
- Real-time ticker, depth, trade, and candle subscriptions over WebSocket.
- Recent trades, order-book snapshots, and candle history through REST.
- Portfolio balances, active orders, order status, and private executions.
- Market, limit, post-only, and immediate-or-cancel orders, plus individual and bulk cancellation.
- Configurable REST and WebSocket service addresses.

Public market data works without credentials. Portfolio and trading operations require a Coinstore API key and secret. Private state is refreshed through authenticated REST requests.
