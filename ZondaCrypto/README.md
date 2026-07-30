# Zonda Crypto Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Zonda Crypto connector** connects StockSharp to the zondacrypto centralized cryptocurrency spot exchange. It translates exchange REST and WebSocket data and account operations into the unified StockSharp message model.

## Key capabilities

- Spot-market discovery with currency, price-step, quantity-step, and minimum-amount metadata.
- Real-time Level 1 quotes, tick trades, and order-book snapshots and updates through public streams.
- REST snapshots and available recent trade history before live continuation; candle data is not exposed.
- Market and limit order submission with supported GTC, IOC, FOK, and post-only options.
- Individual and filtered group cancellation, plus order and execution-status updates; atomic replacement is not supported.
- Wallet balances and portfolio updates through private streams with periodic REST reconciliation.
- API key and secret authentication for private operations; public market data can be used without trading credentials.
- zondacrypto authentication, market codes, transports, filters, and payloads are hidden behind the standard StockSharp API.

## Typical use

Use this connector for zondacrypto spot-market terminals, live strategies, recent-trade analysis, account monitoring, and order-management services.

Available markets, recent-history depth, trading permissions, order options, request limits, and service availability are controlled by zondacrypto and the connected account.
