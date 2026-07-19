# Foxbit Connector

The connector integrates Foxbit cryptocurrency spot markets through the current
REST API v3 and public WebSocket API v3.

Supported features:

- discovery of all spot markets with native price, quantity, and minimum-order
  increments;
- REST Level1 snapshots, order books, public trades, and OHLCV candle history;
- realtime Level1, public trades, and sequence-checked incremental order books
  through the official WebSocket v3 endpoint;
- automatic WebSocket reconnect, subscription restoration, heartbeat, and a new
  order-book snapshot after a sequence gap;
- realtime candles constructed from the official trade stream for every
  REST-supported interval;
- balances, current and historical orders, and individual account trades;
- limit, market, quote-amount instant, stop-market, and stop-limit orders;
- GTC, IOC, FOK, post-only, slippage tolerance, self-trade prevention, individual
  cancellation, and native filtered bulk cancellation;
- server-clock synchronization, bounded retry of safe reads, typed API errors,
  and lost-placement reconciliation by numeric client order ID.

API credentials are optional for public market data. Trading and account data
require a Foxbit API key and secret with the required permissions. Private REST
requests use `X-FB-ACCESS-KEY`, `X-FB-ACCESS-TIMESTAMP`, and the lowercase
hexadecimal HMAC-SHA256 `X-FB-ACCESS-SIGNATURE`. Private state is refreshed at a
rate-conscious configurable interval because WebSocket v3 is a public market-data
feed.

Foxbit's published WebSocket sample lists a `candles-60` channel, but the current
public endpoint does not acknowledge that subscription. The adapter therefore
builds live candles from the supported `trades` channel and uses REST v3 for
authoritative candle history.

Every REST and WebSocket payload is represented by a concrete DTO. The transport
does not use dynamic JSON trees, anonymous protocol objects, protocol
dictionaries, or untyped object arrays.

Official resources:

- [Foxbit API documentation](https://docs.foxbit.com.br/)
- [Foxbit REST API v3](https://docs.foxbit.com.br/rest/v3/)
- [Foxbit WebSocket API v3](https://docs.foxbit.com.br/websocket/v3)
- [Official Foxbit API samples](https://github.com/foxbit-group/foxbit-api-samples)
- [Foxbit website](https://foxbit.com.br/)
