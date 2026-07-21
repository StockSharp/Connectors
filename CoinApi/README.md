# StockSharp CoinAPI Connector

The connector integrates StockSharp with CoinAPI's normalized cryptocurrency
Market Data API. It combines the production REST API with WebSocket V1 and is a
market-data connector only. CoinAPI EMS/FIX order routing is intentionally not
used, so the connector does not submit or manage orders.

## Access and configuration

A CoinAPI API key is required. REST sends it only in the `X-CoinAPI-Key`
header. WebSocket authentication uses the same header and the typed
`apikey` field in subscription commands; the key is never placed in a URL.

`ApiEndpoint` defaults to the GeoDNS REST root at `rest.coinapi.io`, while
`SocketEndpoint` defaults to the secure WebSocket V1 root at
`ws.coinapi.io/v1/`. Both can be replaced with CoinAPI's regional endpoints or
controlled gateways. The connector accepts HTTPS for REST and WSS for the
stream and rejects endpoint query strings and fragments.

CoinAPI publishes a very large global instrument catalog. `ExchangeFilter`
and `AssetFilter` narrow discovery on the server. `MaximumItems` caps a lookup
at 100,000 instruments. The response is parsed as a stream of concrete symbol
DTOs and stops at the cap instead of materializing the entire global JSON array.

Each StockSharp security uses CoinAPI's canonical `symbol_id` as both its
security code and native identity. A canonical ID such as
`BITSTAMP_SPOT_BTC_USD` includes the venue and instrument class, avoiding the
ambiguity of a bare `BTC/USD` pair. Direct market-data requests should retain
the identity returned by security lookup.

`RequestInterval` provides REST pacing. `HistoryLimit` caps a historical
subscription at CoinAPI's 100,000-row endpoint limit. `HistoryLookback` is used
only when a historical request omits its start time. `MarketDepth` caps REST
and live order books at 50 levels per side.

## Market data

The connector supports:

- metadata discovery for spot, futures, perpetuals, options, indexes, credit,
  contracts, and combo instruments;
- historical and live normalized trades, including CoinAPI trade UUID and
  reported or estimated aggressor side;
- historical and live best bid/ask quotes as StockSharp Level 1 data;
- historical L2 snapshots and live `book5`, `book20`, or `book50` snapshots;
- historical OHLCV for every fixed CoinAPI period from 1 second through 10
  days, and live OHLCV through 1 day.

Historical trades, quotes, books, and OHLCV are requested in ascending time
order with explicit `time_start`, `time_end`, and `limit`. Live subscriptions
share one WebSocket connection. Exact-symbol filters end with `$`, preventing
CoinAPI's prefix matching from subscribing to similarly named instruments.
The client uses typed `hello`, `subscribe`, and `unsubscribe` DTOs, restores
all active scopes after reconnect, and enables CoinAPI heartbeat messages.

CoinAPI book5/book20/book50 messages are complete bounded snapshots, so they
are emitted with `SnapshotComplete` state. Streamed OHLCV updates remain active
until the period end is reached and are then emitted as finished candles. All
REST and WebSocket timestamps are converted to UTC `DateTime` values.

Every protocol payload has a concrete DTO. The implementation does not use
dynamic JSON trees, anonymous protocol objects, protocol dictionaries, or
untyped object arrays.

## Official documentation

- [CoinAPI Market Data API](https://www.coinapi.io/products/market-data-api/docs)
- [Authentication](https://www.coinapi.io/products/market-data-api/docs/authentication)
- [Active symbol metadata](https://www.coinapi.io/products/market-data-api/docs/rest-api/metadata/symbols/exchange_id/active/get)
- [Historical trades](https://www.coinapi.io/products/market-data-api/docs/rest-api/trades/trades/symbol_id/history/get)
- [Historical quotes](https://www.coinapi.io/products/market-data-api/docs/rest-api/quotes/quotes/symbol_id/history/get)
- [Historical order books](https://www.coinapi.io/products/market-data-api/docs/rest-api/order-book/orderbooks/symbol_id/history/get)
- [OHLCV periods](https://www.coinapi.io/products/market-data-api/docs/rest-api/ohlcv/ohlcv/periods/get)
- [Historical OHLCV](https://www.coinapi.io/products/market-data-api/docs/rest-api/ohlcv/ohlcv/symbol_id/history/get)
- [WebSocket subscriptions](https://www.coinapi.io/products/market-data-api/docs/websocket/general)
- [WebSocket messages](https://www.coinapi.io/products/market-data-api/docs/websocket/messages)
- [WebSocket endpoints](https://www.coinapi.io/products/market-data-api/docs/websocket/endpoints)
- [CoinAPI website](https://www.coinapi.io/)
