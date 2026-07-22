# Luno Exchange Connector

The connector integrates Luno spot markets through the current REST and
WebSocket APIs.

Supported features:

- discovery of markets with native price and volume constraints;
- Level1 data, order books, and public trades from REST snapshots;
- realtime Level1 data, order books, public trades, and trade-built candles
  through the official per-market WebSocket stream;
- historical OHLCV candles for every interval documented by Luno;
- HTTP Basic authentication with an API key ID and secret;
- balances, open and historical orders, and account fills;
- market, limit, post-only, and stop-limit orders;
- GTC, IOC, and FOK time-in-force and individual or filtered bulk
  cancellation;
- realtime balance, order-status, and fill updates through the official user
  WebSocket stream.

Public REST market data works without credentials, except for the candles
endpoint, which currently requires authentication. Luno requires an API key
and secret as the first message on both documented WebSocket streams. Without
credentials, use history-only subscriptions; with credentials, realtime
market and account streams are enabled automatically.

Official documentation:

- [Luno API documentation](https://www.luno.com/en/developers/api)
- [Luno official Go SDK](https://github.com/luno/luno-go)
