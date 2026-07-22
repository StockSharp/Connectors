# Fugle Market Data API connector

This directory contains the current Fugle Market Data API v1.0 connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Protocol coverage

- REST access to current Taiwan stock and index lists and to TAIFEX futures/options lists.
- Current stock and futures/options WebSocket endpoints with API-key authentication, heartbeat handling, reconnect, and subscription recovery.
- Realtime trades, one-minute candles, five-level books, aggregate Level1 data, and index values.
- Stock historical candles at 1/3/5/10/15/30/60-minute, daily, weekly, and monthly intervals.
- Current-session futures/options candles at the intervals published by the beta Fugle futures/options API.

## Authentication and plan limits

Configure an API key issued for Fugle Market Data v1.0. REST requests send it in `X-API-KEY`; each WebSocket authenticates with the documented `auth` event. Fugle plan limits govern REST frequency, simultaneous connections, and the number of symbol/channel subscriptions. The connector opens the stock or futures/options WebSocket lazily, so an unused market does not consume a streaming connection.

## Trading status

Fugle officially stopped updating its former Fugle Trade API in November 2025 and directs order-entry users to partner broker SDKs. Consequently this connector intentionally exposes current market data only and does not advertise orders, portfolio, or position capabilities. A broker-specific successor should be implemented as its own connector when that broker publishes a stable, redistributable wire API.

Stock minute history is returned by Fugle for the most recent 30 days regardless of requested dates. Futures/options REST candles cover the current session rather than long-term history. Realtime candles are one minute; larger realtime bars can be built by StockSharp aggregation.

## Official references

- [Fugle Market Data API](https://developer.fugle.tw/docs/data/intro/)
- [Stock WebSocket API](https://developer.fugle.tw/docs/data/websocket-api/getting-started/)
- [Futures/options API](https://developer.fugle.tw/docs/data-futopt/http-api/getting-started/)
- [Historical candles](https://developer.fugle.tw/docs/data/http-api/historical/candles/)
- [Plans and limits](https://developer.fugle.tw/docs/pricing/)
- [Discontinued Fugle Trade API notice](https://developer.fugle.tw/docs/trading/intro/)
