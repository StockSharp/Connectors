# Coinmetro Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Coinmetro connector** integrates StockSharp with the Coinmetro cryptocurrency spot exchange. It combines REST instrument, account, order, and candle endpoints with WebSocket updates for live market and private activity, and supports separate live and demo environments.

## Key capabilities

- Discover Coinmetro spot instruments and their trading constraints.
- Subscribe to live Level 1 quotes, market depth, and tick trades over WebSocket.
- Download historical candles for the supported 1-minute, 5-minute, 30-minute, 4-hour, and daily intervals.
- Submit limit and market orders with supported GTC, IOC, FOK, and GTD parameters.
- Cancel individual orders or groups of matching open orders.
- Load balances, open and historical orders, and own trades.
- Switch between configurable live and demo REST and WebSocket endpoints.

## Typical use

Use this connector for Coinmetro spot-market monitoring, historical candle loading, and automated trading. Configure an access token with the necessary permissions for private live operations; demo mode uses the separate open endpoints and can obtain its demo token automatically.

Candles are historical only and do not continue with live updates. Atomic order replacement, conditional, iceberg, and post-only orders are not supported, and order books are published as snapshots rather than StockSharp increments. Private reconciliation frequency and API rate limits should be considered in strategy design.
