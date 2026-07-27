# BitoPro Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **BitoPro connector** connects StockSharp to BitoPro, a regulated Taiwan-focused cryptocurrency exchange with active TWD spot markets.

## Key capabilities

- Spot instrument discovery with price, quantity, and order-limit metadata.
- Level 1 market state, Level 2 order-book snapshots, and public trades.
- Real-time tickers, order books, and trades over WebSocket.
- Historical OHLCV candles for every interval exposed by BitoPro.
- Portfolio balances, open and historical orders, and private trade history.
- Limit, market, stop-limit, post-only, individual, and bulk cancellation operations.
- Configurable REST and WebSocket service addresses.

## Typical use

Use this connector in trading robots, terminals, TWD market-data collectors, monitoring systems, and order-management services.

Public market data requires no credentials. Account and trading operations require the account email, API key, and secret. BitoPro accepts market-buy amounts in quote currency; the connector converts StockSharp base volume using the latest public price.
