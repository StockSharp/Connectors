# CoinTR Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **CoinTR connector** connects StockSharp to CoinTR, a cryptocurrency exchange focused on the Turkish market. It provides access to CoinTR spot instruments through the standard StockSharp message model.

## Key capabilities

- Spot instrument discovery with price and quantity precision and trading limits.
- Level 1 quotes, Level 2 order-book snapshots, and public trades.
- Real-time tickers, order books, trades, and candles over WebSocket.
- Historical OHLCV candles for CoinTR-supported intervals.
- Portfolio balances, active orders, and private fill notifications.
- Market, limit, and trigger order submission and order cancellation.
- Configurable REST, public WebSocket, and private WebSocket endpoints.

## Typical use

Use the connector in trading robots, terminals, market-data collectors, monitoring tools, and order-management services that operate on CoinTR spot markets.

Public market data does not require credentials. Trading and account operations require an API key, secret, and passphrase with suitable permissions. For a market buy, CoinTR interprets volume as an amount in the quote currency; limit orders and market sells use the base asset quantity.
