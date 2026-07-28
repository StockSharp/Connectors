# MAX Exchange Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **MAX Exchange connector** connects StockSharp to the Taiwan-focused spot exchange operated by MaiCoin Group. It is especially useful for TWD and USDT cryptocurrency markets.

## Key capabilities

- Spot instrument discovery with trading state, precision, and minimum order metadata.
- Level 1 quotes, Level 2 order books, public trades, and OHLCV candles.
- Real-time ticker, order-book, trade, and candle streams over WebSocket.
- Historical candles and recent market snapshots through REST API v3.
- Portfolio balances, open and historical orders, and private executions.
- Market, limit, stop-market, stop-limit, post-only, and IOC limit orders.
- Individual and bulk cancellation, with configurable REST and WebSocket addresses.

## Typical use

Use this connector in trading robots, terminals, TWD market-data collectors, monitoring services, and order-management systems.

Public market data does not require credentials. Account and trading operations require a MAX Exchange API key and secret.
