# Samco Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Samco connector** connects StockSharp to the Samco Trade API for Indian securities and derivatives. It exposes the broker's market-data and trading services through the unified StockSharp message model.

## Key capabilities

- Instrument discovery for supported NSE, BSE, NFO, BFO, CDS, MCX, and MFO stocks, futures, and options.
- Real-time Level 1 quotes, tick trades, and five-level order-book data through the Samco feed.
- Historical time-frame candles, with continued updates produced through streaming or REST polling.
- Limit and supported broker order submission, modification, and individual cancellation; atomic group cancellation is not exposed.
- Portfolio limits, holdings, positions, orders, and trades, with private state reconciled by polling.
- Optional WebSocket market data with REST fallback and configurable polling and service endpoints.
- Authentication uses either an existing daily session token or Samco API credentials, subject to the broker's session rules.
- Samco-specific identifiers, sessions, and payloads are hidden behind the standard StockSharp API.

## Typical use

Use this connector for Indian-market trading terminals, live strategies, portfolio monitors, and order-management applications connected to a Samco account.

Instrument coverage, five-level depth, historical availability, trading permissions, rate limits, and session lifetime are controlled by Samco and the connected account.
