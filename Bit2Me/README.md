# Bit2Me Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Bit2Me connector** connects StockSharp to Bit2Me Pro, the spot trading platform of the Spanish digital-asset provider. It is useful for systems that need direct access to crypto markets with EUR liquidity through the standard StockSharp message model.

## Key capabilities

- Discovery of available Bit2Me Pro spot markets and their price, amount, and minimum-order rules.
- REST snapshots for Level 1 quotes and the Level 2 order book.
- Real-time public trades and complete order-book updates over WebSocket.
- Historical trades and OHLCV candles for the intervals published by Bit2Me.
- Market, limit, and stop-limit order submission.
- Order cancellation and retrieval of orders and fills.
- Portfolio balances and blocked funds used by active orders.
- Configurable REST and WebSocket addresses for testing, routing, or infrastructure changes.

## Typical use

Use this connector in trading robots, terminals, data collectors, order-management services, and monitoring tools that work with Bit2Me Pro spot instruments.

Public market data does not require credentials. Trading and account operations require a Bit2Me API key and secret with the appropriate permissions. Available markets, rate limits, and account capabilities are controlled by Bit2Me.
