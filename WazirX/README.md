# WazirX Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **WazirX connector** connects StockSharp to the WazirX centralized cryptocurrency spot exchange. It translates exchange REST and WebSocket data and account operations into the unified StockSharp message model.

## Key capabilities

- Spot-market discovery with price, quantity, and trading-rule metadata.
- Real-time Level 1 quotes, tick trades, order books, and time-frame candles through public streams.
- REST snapshots and available historical trade and candle requests before live continuation.
- Limit and supported stop-limit order submission, individual and filtered group cancellation, and order/trade status updates.
- Balance and portfolio updates through private streams with REST reconciliation.
- API key and secret authentication for private operations; public market data can be used without trading credentials.
- Market orders and atomic order replacement are not exposed by this adapter.
- WazirX authentication, symbols, transports, filters, and payloads are hidden behind the standard StockSharp API.

## Typical use

Use this connector for WazirX spot-market terminals, live strategies, charting, account monitoring, and order-management services.

Available markets, historical depth, stop-limit support, trading permissions, filters, request limits, and service availability are controlled by WazirX and the connected account.
