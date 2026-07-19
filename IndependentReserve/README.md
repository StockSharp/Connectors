# Independent Reserve Connector

The connector integrates Independent Reserve cryptocurrency spot markets
through the current REST API and the official public WebSocket feed.

Supported features:

- discovery of all trade-enabled primary currencies and supported fiat quote
  currencies with native price and volume precision;
- Level1 data, full order books, public trades, and one-hour OHLCV history;
- realtime order books and public trades through the official `orderbook-*`
  and `ticker-*` WebSocket channels;
- sequence validation and REST snapshot recovery for every live order book;
- balances, open and historical orders, and account trades;
- market and limit orders with GTC, IOC, FOK, and maker-only time-in-force;
- quote-denominated market volume, slippage protection, and individual or
  filtered bulk cancellation.

Independent Reserve does not provide a private account WebSocket. The
connector therefore combines authenticated REST snapshots with periodic
polling. Public order and trade events are correlated with the connector's
non-sensitive `OrderGuid` and `ClientId` values to refresh owned orders as
soon as they appear on the official public stream.

Private requests use the API's timestamp-expiry authentication mode to avoid
cross-thread nonce races. Independent Reserve requires an API key restricted
to at least one IP address when this mode is used.

Every REST and WebSocket payload is represented by a concrete DTO. The
transport does not use dynamic JSON trees, anonymous protocol objects, or
protocol dictionaries.

Official documentation:

- [Independent Reserve API documentation](https://www.independentreserve.com/features/api)
- [Independent Reserve WebSocket documentation](https://github.com/independentreserve/websockets)
- [Independent Reserve official .NET API client](https://github.com/independentreserve/dotNetApiClient)
