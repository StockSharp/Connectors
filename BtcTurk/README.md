# BtcTurk Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **BtcTurk connector** connects StockSharp to BtcTurk Kripto, a Turkish spot cryptocurrency exchange. It is intended for trading and market-data systems that work with TRY, BTC, USDT, and other markets through the standard StockSharp message model.

## Key capabilities

- Discovery of spot instruments and their price, volume, and order limits.
- Level 1 quotes, Level 2 order-book snapshots, and public trades.
- Real-time ticker, order-book, and trade streams over WebSocket.
- Historical OHLCV candles for the intervals supported by BtcTurk.
- Portfolio balances, open and historical orders, and account trades.
- Market, limit, stop-market, and stop-limit order submission.
- Individual and group order cancellation.
- Configurable REST, historical-data, and WebSocket endpoints.

## Typical use

Use this connector in trading robots, terminals, data collectors, order-management services, and monitoring systems that operate on BtcTurk Kripto spot markets.

Public market data does not require credentials. Trading and account operations require a BtcTurk API key and Base64-encoded secret with the appropriate permissions. For market buy orders, BtcTurk interprets quantity as an amount in the quote currency; other order quantities are expressed in the base asset.
