# Tokocrypto Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Tokocrypto connector** connects StockSharp to Tokocrypto's MAIN spot market. It is intended for Indonesian-focused cryptocurrency trading and for applications that need Tokocrypto market data in the StockSharp message model.

## Key capabilities

- MAIN spot instrument discovery with price, volume, and minimum-order filters.
- Level 1 quotes, Level 2 order books, public trades, and OHLCV candles.
- Live ticker, partial order-book, trade, and candle streams over WebSocket.
- Historical candles and recent market snapshots through the public REST API.
- Spot balances, open and historical orders, and private trade history.
- Market, limit, stop-market, stop-limit, post-only, IOC, and FOK orders.
- Individual and group cancellation with configurable account REST, market-data REST, and WebSocket addresses.

## Typical use

Use this connector in trading robots, terminals, market-data collectors, monitoring services, and order-management systems that work with Tokocrypto spot instruments.

Public market data does not require credentials. Account and trading operations require a Tokocrypto API key and secret.
