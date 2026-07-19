# Pintu Pro Connector

The connector integrates Pintu Pro spot markets through the official v1 REST
and WebSocket protocols.

Supported features:

- symbol discovery with price, quantity, and order-value limits;
- Level1 quotes, full 10-level order-book snapshots, and public trades;
- realtime order books and trades through the official public WebSocket;
- balances, open orders, order history, and account fill history;
- realtime private order, fill, and balance events;
- limit and market orders, GTC, IOC, FOK, and post-only execution;
- client order IDs, individual cancellation, all-orders cancellation by symbol,
  and side-filtered cancellation;
- weighted rate limiting, server-clock adjustment from HTTP dates, required
  WebSocket heartbeat replies, reconnect/resubscribe, snapshot recovery, and
  lost-response reconciliation by `client_order_id`.

An API key is optional for public market data. Trading, balances, and private
streams require a Pintu Pro API key and HMAC-SHA256 secret. A market buy uses
`PintuProOrderCondition.QuoteAmount`, because the exchange accepts its amount in
quote currency; market sells use the regular order volume.

The public documentation currently publishes the UAT REST and WebSocket hosts,
so those are the connector defaults. Both endpoint settings are configurable
and can be replaced with the production hosts enabled for the account.

Every REST and WebSocket payload is represented by a concrete DTO. Even the
array-based book levels and currency-keyed balance object are decoded by typed
converters; the transport does not use dynamic JSON trees, anonymous protocol
objects, or protocol dictionaries.

Official resources:

- [Pintu Pro API documentation](https://docs.pintu.pro/)
- [Pintu press kit and brand assets](https://pintu.co.id/en/press-kit)
