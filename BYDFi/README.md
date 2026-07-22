# BYDFi Connector

The connector integrates BYDFi perpetual futures through the current
production REST API and the official public futures WebSocket service.

Supported features:

- discovery of USDT-margined and inverse perpetual contracts with native
  price, quantity, contract-factor, and order-size metadata;
- Level1 snapshots from REST and realtime ticker, mark-price, and index-price
  updates through WebSocket;
- REST order-book snapshots and realtime 10-, 50-, or 100-level WebSocket
  snapshots;
- recent public trades with continued REST polling;
- historical OHLCV candles and realtime candle updates for every interval
  published by BYDFi;
- API-key authentication, futures balances, positions, open and historical
  orders, and account trade history;
- market, limit, stop, take-profit, stop-market, take-profit-market, and
  trailing-stop orders; GTC, IOC, FOK, post-only, reduce-only, and
  close-position flags; order amendment, individual cancellation,
  filtered cancellation, native cancel-all, and position closing;
- bounded retry of safe REST reads, request signing over the exact transmitted
  query and body, WebSocket reconnect, response-size limits, and configurable
  polling cadence.

Public market data does not require credentials. Private operations require a
BYDFi API key and secret. `W001` is used as the default futures wallet and can
be changed in the adapter settings.

The currently published BYDFi API provides futures market data and trading.
Its SPOT section documents deposit and withdrawal history but does not expose
SPOT instruments, market data, or order entry, so this connector intentionally
does not claim SPOT support.

BYDFi does not currently publish a usable private WebSocket endpoint or a
public trade stream in its official documentation. Consequently, account
state and public trades are refreshed through REST. Level1, order books, and
candles use the official realtime WebSocket streams. Changing the active
WebSocket subscription set reconnects the URL-based combined stream, as
required by the production service.

Official resources:

- [BYDFi API introduction](https://developers.bydfi.com/en/intro)
- [Futures market REST API](https://developers.bydfi.com/en/futures/market)
- [Futures trading REST API](https://developers.bydfi.com/en/futures/trade)
- [Futures market WebSocket API](https://developers.bydfi.com/en/futures/websocket-market)
- [Request signing](https://developers.bydfi.com/en/signature)
- [BYDFi](https://www.bydfi.com/)
