# BigONE Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **BigONE connector** connects StockSharp to BigONE spot and contract markets. It provides a single message adapter for applications that need both ordinary crypto pairs and coin- or USDT-margined derivatives.

## Key capabilities

- Discovery of spot pairs and available perpetual contract instruments.
- Level 1 quotes, order books, public trades, and OHLCV candles.
- Live spot streams through the JSON WebSocket API and dedicated URL streams for contracts.
- Spot candle history and current REST snapshots for both market families.
- Spot and contract balances, contract positions, orders, and private trade history.
- Market, limit, IOC, FOK, post-only, spot stop, and reduce-only contract orders.
- Individual and group cancellation.
- Configurable spot and contract REST, public WebSocket, and private WebSocket addresses.

## Typical use

Use the connector in trading robots, terminals, market-data collectors, monitoring services, and order-management systems that combine BigONE spot liquidity with derivatives.

Public market data does not require credentials. Account and trading operations require a BigONE API key and secret.
