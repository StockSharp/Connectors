# OSL Global Connector

The connector integrates the current OSL Global SPOT API through the
production REST service and both documented WebSocket generations.

Supported features:

- discovery of SPOT instruments with native price, quantity, and order-size
  limits;
- Level1 REST snapshots and realtime ticker updates;
- recent public trades through REST and realtime trades through WebSocket;
- REST order-book snapshots and native realtime 5- or 15-level snapshots;
- historical OHLCV candles for every fixed interval exposed by OSL and
  realtime candlestick updates through the official kline stream;
- API-key authentication, account balances, open and historical orders,
  trade fills, and realtime asset, order, and fill updates;
- market and limit registration, GTC, IOC, FOK, post-only, quote-amount market
  buys, all documented self-trade prevention policies, individual
  cancellation, filtered cancellation, and native cancel-all;
- bounded retry of safe REST reads, request signing over the exact transmitted
  query and body, WebSocket heartbeat, reconnect with subscription recovery,
  documented message pacing, and response-size limits.

Public market data does not require credentials. Private operations require an
OSL API key and secret. The passphrase may be empty when the API key was
created without one. OSL enforces API-key IP whitelisting.

OSL currently publishes SPOT ticker, trade, depth, private account, and private
order channels on the v2 WebSocket endpoints. Its documented realtime kline
channel remains on the `/openapi/v1/ws` endpoint, so the connector keeps that
official stream separate instead of emulating candles by polling REST.

Every REST and WebSocket payload is represented by a concrete DTO, including
book-level arrays, candle arrays, fee variants, command envelopes, login
messages, and subscription messages. The transport does not use dynamic JSON
trees, anonymous protocol objects, protocol dictionaries, or untyped object
arrays.

Official resources:

- [OSL Global API overview](https://docs.glb.osl.com/reference/overview-osl-global-api)
- [SPOT trading REST API](https://docs.glb.osl.com/reference/spot-trading-module)
- [SPOT WebSocket overview](https://docs.glb.osl.com/reference/spot-websocket-overview)
- [WebSocket authentication](https://docs.glb.osl.com/reference/websocket-authentication)
- [OSL Global](https://osl.com/)
