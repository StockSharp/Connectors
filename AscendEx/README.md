# AscendEX Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **AscendEX connector** integrates StockSharp with the published AscendEX Pro API. It covers cash spot, margin, and perpetual futures markets through one adapter, making it useful for multi-market crypto strategies and for preserving access to the venue's documented protocol.

## Key capabilities

- Discovery of spot, margin, and perpetual futures instruments with trading state, price step, volume step, and order limits.
- Level 1 quotes, Level 2 order books, public trades, and OHLCV candles.
- REST snapshots and history plus separate real-time WebSocket streams for spot and futures.
- Cash and margin balances, futures collateral and positions, active orders, order history, and executions.
- Market, limit, stop-market, and stop-limit orders with GTC, IOC, FOK, post-only, and futures reduce-only options.
- Individual and bulk order cancellation.
- Configurable REST, spot WebSocket, and futures WebSocket addresses, account group, and cash or margin account mode.

Public market data does not require credentials. Portfolio and trading operations require an API key, secret, and the account group assigned by AscendEX.
