# Finam Trade API Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Finam Trade API connector** connects StockSharp applications to brokerage accounts and market data provided by Finam. It converts Finam instruments, quotes, orders, trades, and portfolio state into the standard StockSharp message model.

## Key capabilities

- Instrument discovery for stocks, bonds, currencies, funds, futures, and options available through Finam.
- Level 1 quotes, order books, public trades, and time-frame candles.
- Historical candle requests and real-time market-data subscriptions.
- Market, limit, stop, and stop-limit order submission and order cancellation.
- Order-state, own-trade, cash, and position updates.
- Automatic exchange of the API secret for a short-lived session token.
- REST and WebSocket addresses are configurable for compatible gateways and test environments.

## Typical use

Use the connector in trading robots, terminals, portfolio monitors, and order-management services that need one StockSharp interface for Finam market data and trading.

A Finam Trade API secret is required. The connector can use an explicitly selected account or automatically select the first account available to the token. Instruments are identified by Finam's `ticker@MIC` symbols. Available markets, history depth, real-time data, trading permissions, and request limits depend on the connected account and Finam service terms.
