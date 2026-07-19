# Mercado Bitcoin Connector

The connector integrates Mercado Bitcoin's current spot markets through REST
API v4 and the official public WebSocket API.

Supported features:

- discovery of exchange-listed and exchange-traded markets with native price,
  quantity, and notional constraints;
- Level1 snapshots and realtime ticker updates;
- order-book snapshots with up to 1000 REST levels and realtime snapshots with
  the maximum documented WebSocket depth of 200;
- recent and time-filtered public trades plus realtime trade subscriptions;
- historical candles for every interval published by Mercado Bitcoin;
- OAuth2 client-credentials authentication with automatic token renewal;
- multiple-account discovery, balances, and periodic account refreshes;
- market, limit, post-only, and stop-limit orders;
- individual and filtered bulk cancellation;
- order lookup, order history, executions, and periodic order refreshes.

Public market data works without credentials. Private operations require an API
client ID and secret. `AccountId` may be left empty when the API key owns one
account; for multiple accounts it selects the default account for trading.

Mercado Bitcoin currently documents only public WebSocket channels. Private
account and order updates are therefore refreshed through the official REST API
instead of an undocumented stream. Every REST and WebSocket payload is
represented by a concrete DTO; the transport does not use dynamic JSON trees or
protocol dictionaries.

Official documentation:

- [Mercado Bitcoin REST API v4](https://api.mercadobitcoin.net/api/v4/docs)
- [Mercado Bitcoin WebSocket API](https://ws.mercadobitcoin.net/docs/v0/)
- [API product page](https://www.mercadobitcoin.com.br/api)
