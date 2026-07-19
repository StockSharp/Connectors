# Korbit Connector

The connector integrates Korbit spot markets through the current Open API v2
REST and WebSocket protocols.

Supported features:

- trading-pair discovery and trading-state metadata;
- Level1, Level2 order books, public trades, and OHLCV candle history;
- realtime tickers, full order-book snapshots, and trades through the official
  public WebSocket;
- realtime candles aggregated from the official trade stream;
- balances, open and historical orders, and account fills;
- realtime private order, fill, and balance events through the official private
  WebSocket;
- limit, market, and best bid/offer orders;
- GTC, IOC, FOK, post-only, price protection, client order IDs, individual
  cancellation, and filtered group cancellation;
- server-clock synchronization, rate-limit handling, reconnect snapshots, and
  idempotent order reconciliation by `clientOrderId`.

An API key is optional for public market data. Trading, balances, and the
private WebSocket require a Korbit key created with HMAC-SHA256 authentication.
Set the account sequence to `1` for the main account or to an account explicitly
allowed by the API key.

Korbit uses price-dependent tick sizes. The connector loads and validates the
exact policy before placing each symbol's first limit order. Security metadata
exposes the smallest published tick as its baseline price step.

Every REST and WebSocket payload is represented by a concrete DTO. The
transport does not use dynamic JSON trees, anonymous protocol objects, or
protocol dictionaries.

Official documentation:

- [Korbit Open API documentation](https://apidocs.korbit.co.kr/)
- [REST API guide](https://docs.korbit.co.kr/llms/en/rest_api.md)
- [WebSocket API guide](https://docs.korbit.co.kr/llms/en/websocket_api.md)
