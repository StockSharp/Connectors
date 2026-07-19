# Coinone Connector

The connector integrates Coinone's current Korean spot market through Public
API v2, Private API v2.1, and the official public and private WebSocket streams.

Supported features:

- KRW spot-market discovery with price, quantity, notional, maintenance, and
  side-specific trading constraints;
- Level1 snapshots and realtime ticker updates;
- full order-book snapshots with native Coinone depth sizes;
- recent public executions and realtime trade subscriptions;
- historical candles with paging and realtime candle updates for the intervals
  published by the WebSocket API;
- balances with available, locked, and average acquisition price values;
- market, limit, post-only limit, and stop-limit orders;
- individual, market-wide, and filtered cancellation;
- active-order lookup, completed-order and fill history, and realtime private
  order events;
- realtime private asset updates and subscription restoration after reconnect.

Public market data works without credentials. Private operations require a
Coinone access token and secret with the required permissions and an allowed
source IP. The `QuoteCurrency` setting defaults to `KRW`.

Private REST requests sign the Base64 representation of the exact typed JSON
request with HMAC-SHA512. Private WebSocket authentication uses the same scheme
with a fresh UUID nonce and UTC millisecond timestamp. Every REST and WebSocket
payload is represented by a concrete DTO; the transport does not use dynamic
JSON trees or protocol dictionaries.

Coinone publishes monthly candles through REST, but does not list that interval
for the CHART WebSocket channel. It is therefore available as a history-only
interval in this connector.

Official documentation:

- [Coinone Open API](https://docs.coinone.co.kr/)
- [Public API overview](https://docs.coinone.co.kr/docs/about-public-api)
- [Public WebSocket](https://docs.coinone.co.kr/reference/public-websocket-1)
- [Private WebSocket](https://docs.coinone.co.kr/reference/private-websocket-1)
- [Order API](https://docs.coinone.co.kr/reference/place-order)
- [API rate limits](https://docs.coinone.co.kr/docs/ratelimit-%EC%95%88%EB%82%B4)
