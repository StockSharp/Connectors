# StockSharp Kaiko Connector

The connector integrates StockSharp with Kaiko's institutional market-data
platform. It uses the public Reference Data REST API, the regional Market Data
REST API, and the production gRPC Stream endpoint through Kaiko's official C#
SDK. It is a market-data connector and does not submit orders.

## Access and configuration

Reference instrument discovery is available without an API key. Historical
trades, historical OHLCV, and every live stream require a Kaiko API key. The
key is sent as `X-Api-Key` to the regional REST API and as bearer credentials
to gRPC; it is never placed in a URL.

`Region` selects the official US or EU Market Data REST root. Changing it also
updates `MarketEndpoint` while that endpoint still has its default value.
`ReferenceEndpoint`, `MarketEndpoint`, and `StreamEndpoint` can be overridden
for controlled network environments. All endpoint settings require HTTPS.

`ExchangeFilter` and `InstrumentClassFilter` can narrow large reference-data
lookups. A security returned by lookup keeps Kaiko's exchange code, instrument
class, and normalized instrument code in its native identity. This is
important because a code such as `btc-usd` exists on many venues. Without a
native identity, a market-data request must resolve to exactly one instrument;
otherwise the connector asks the caller to perform security lookup first.

`RequestInterval` provides client-side REST pacing. `MaximumItems` caps a
reference lookup at 100,000 instruments, and `HistoryLimit` caps each
StockSharp history subscription at 100,000 rows. REST retries transient HTTP
failures and rate limits with bounded backoff. Stream sessions reconnect on
transient gRPC failures using the adapter reconnection count.

## Market data

The connector supports:

- security discovery for Kaiko spot, future, perpetual-future, and option
  instruments through the public `/v1/instruments` endpoint;
- historical and live normalized trades;
- live Level 1 updates combining normalized trades with best bid and best ask;
- historical and live OHLCV from 1 second through 1 day for the native periods
  advertised by the connector.

Historical data is requested in ascending order and continued with Kaiko's
opaque `continuation_token`. The connector rebuilds continuation requests
against the configured API root instead of following arbitrary `next_url`
values. Duplicate trades and candles across page boundaries are suppressed.
The end of every REST range is exclusive, matching Kaiko's API contract.

Live data uses `gateway-v0-grpc.kaiko.ovh` and `KaikoSdk`. The HTTP stream
gateway is intentionally not used because Kaiko documents it as a testing
interface. Identical upstream trade, top-of-book, or OHLCV sessions are shared
between StockSharp subscriptions and released after the final local consumer
unsubscribes.

Kaiko OHLCV `uid` identifies the completed aggregation interval, while its
`timestamp` is the delivery time. The connector therefore uses `uid` as the
candle open time and emits streamed intervals as finished candles. All REST
millisecond timestamps and protobuf timestamps are normalized to UTC
`DateTime` values.

## Official documentation

- [Kaiko Developer Hub](https://docs.kaiko.com/)
- [Reference instruments](https://docs.kaiko.com/rest-api/data-feeds/reference-data/basic-tier/exchange-trading-pair-codes-instruments)
- [Historical trades](https://docs.kaiko.com/rest-api/data-feeds/level-1-and-level-2-data/level-1-tick-level/all-trades)
- [Historical OHLCV](https://docs.kaiko.com/rest-api/data-feeds/level-1-and-level-2-data/level-1-aggregations/trade-count-ohlcv-and-vwap/ohlcv-only)
- [Live trades](https://docs.kaiko.com/stream/data-feeds/level-1-and-level-2-data/level-1-tick-level/all-trades)
- [Live top of book](https://docs.kaiko.com/stream/data-feeds/level-1-and-level-2-data/level-1-tick-level/best-bids-and-asks-top-of-book)
- [Live OHLCV](https://docs.kaiko.com/stream/data-feeds/level-1-and-level-2-data/level-1-aggregations/ohlcv)
- [Kaiko website](https://www.kaiko.com/)
