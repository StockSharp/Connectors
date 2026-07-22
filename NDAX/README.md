# NDAX Connector

The connector integrates NDAX spot markets through the exchange's current
production REST API and AlphaPoint WebSocket gateway.

Supported features:

- discovery of instruments and currencies with native price, quantity, and
  minimum-order increments;
- Level1 snapshots and realtime Level1 updates;
- recent public trades through REST and realtime trades through WebSocket;
- historical OHLCV candles and native realtime candle subscriptions for all
  intervals exposed by NDAX;
- REST order-book snapshots, aggregated order-level WebSocket updates,
  sequence validation, duplicate suppression, and automatic snapshot recovery
  after a sequence gap;
- API-key authentication, account balances, open orders, order history,
  account trades, and realtime account events;
- market, limit, stop-market, and stop-limit registration, individual
  cancellation, filtered cancellation, and native cancel-all;
- bounded retry of safe REST reads, documented rate limits, WebSocket
  reconnect and subscription restoration, heartbeat, and response-size limits.

Public market data does not require credentials. Private operations require an
NDAX API key, secret, and user ID. `AccountId` may remain zero to use the
default account returned by authentication. NDAX currently limits clients to
50 REST requests per minute and 10 WebSocket subscriptions per connection;
the connector enforces both constraints.

Official resources:

- [NDAX API reference](https://apidoc.ndax.io/)
- [API comprehensive guide](https://ndax.io/en/support/api_access_and_developer_tools/api-comprehensive-guide)
- [NDAX API access and developer tools](https://ndax.io/en/support/api_access_and_developer_tools)
- [NDAX](https://ndax.io/en)
