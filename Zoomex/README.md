# Zoomex Connector

The connector integrates Zoomex Spot, USDT perpetual, and inverse perpetual
markets through the current production REST and WebSocket APIs.

Supported features:

- discovery of Spot, linear, and inverse instruments with native price,
  quantity, leverage, and order-size metadata;
- Level1 snapshots and realtime ticker updates, including mark price, index
  price, open interest, and funding data for derivatives;
- REST order-book snapshots and stateful realtime order books built from the
  official snapshot and delta stream;
- recent and realtime public trades;
- historical OHLCV candles and realtime candle updates for every interval
  published by Zoomex;
- API-key authentication, wallet balances, positions, open and historical
  orders, executions, and private realtime wallet, position, order, and
  execution events;
- Spot and derivatives market and limit orders, conditional orders, GTC, IOC,
  FOK, post-only, reduce-only, close-on-trigger, hedge-position indexes, Spot
  market quantity units, order amendment, individual cancellation,
  filtered cancellation, native cancel-all, and position closing;
- bounded retry of safe REST reads, request signing over the exact transmitted
  query or JSON body, WebSocket reconnect with subscription recovery,
  documented pacing, and response-size limits.

Public market data does not require credentials. Private operations require a
Zoomex API key and secret with the corresponding account permissions. The
adapter setting selects the wallet account type used for portfolio snapshots.

The current Zoomex API uses V3 REST endpoints, V5 public WebSocket streams, and
the V3 private WebSocket stream. The retired V3 public stream is not used.
Historical order and execution requests are split into the API's documented
seven-day windows and bounded to its two-year retention period.

Official resources:

- [Zoomex API introduction](https://zoomexglobal.github.io/docs/v3/intro)
- [Market API](https://zoomexglobal.github.io/docs/v3/market/time)
- [Create order](https://zoomexglobal.github.io/docs/v3/order/create-order)
- [WebSocket connection](https://zoomexglobal.github.io/docs/v3/ws/connect)
- [Public order-book stream](https://zoomexglobal.github.io/docs/v3/websocket/public/orderbook)
- [Private order stream](https://zoomexglobal.github.io/docs/v3/websocket/private/order)
- [API key application](https://www.zoomex.com/en/promotion/apiapplication)
