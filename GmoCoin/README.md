# GMO Coin Connector

The connector integrates GMO Coin's current Japanese spot and margin markets
through the official Public API, Private API, and public and private WebSocket
streams.

Supported features:

- spot and margin-market discovery with native price, quantity, and notional
  constraints;
- Level1 snapshots and realtime ticker updates;
- full order-book snapshots and realtime depth updates;
- recent public executions and realtime trade subscriptions;
- historical candles for every interval published by GMO Coin;
- account balances, active orders, executions, open positions, and position
  summaries;
- market, limit, stop, post-only, and supported time-in-force orders;
- individual, bulk, and filtered cancellation;
- margin position opening and closing, loss-cut price updates, and position
  events;
- realtime private order, execution, position, and position-summary streams;
- private WebSocket token renewal and subscription restoration after reconnect.

Public market data works without credentials. Private operations require an API
key and secret with the corresponding trading and account permissions.

Private REST requests use GMO Coin's HMAC-SHA256 signature over the UTC millisecond timestamp, HTTP method, API path, and exact serialized request body.

GMO Coin exposes historical candles through REST but does not publish a candle
WebSocket channel. Candle subscriptions are therefore history-only.

Official documentation:

- [GMO Coin API](https://api.coin.z.com/docs/en/)
- [Public REST API](https://api.coin.z.com/docs/en/#public-api)
- [Private REST API](https://api.coin.z.com/docs/en/#private-api)
- [Public WebSocket API](https://api.coin.z.com/docs/en/#public-ws-api)
- [Private WebSocket API](https://api.coin.z.com/docs/en/#private-ws-api)
