# NovaDAX Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **NovaDAX connector** integrates StockSharp with NovaDAX's spot cryptocurrency market. Its focus on Brazilian-real pairs makes it useful for market monitoring, data collection, and automated trading in the Brazilian crypto market.

## Key capabilities

- Discovery of spot instruments with trading state, price and amount precision, and minimum-order limits.
- Level 1 quotes, Level 2 order books, public trades, and OHLCV candle history.
- Real-time ticker, depth, and trade subscriptions through Socket.IO.
- REST snapshots, recent trades, and historical candles.
- Balances, active and historical orders, order status, and private fills.
- Market, limit, stop-market, and stop-limit orders with individual and symbol-wide cancellation.
- Configurable REST and Socket.IO addresses, sub-account identifier, and Engine.IO protocol version.

Public market data is available without credentials. Portfolio and trading operations require a NovaDAX API key and secret; a sub-account identifier can be supplied when needed.
