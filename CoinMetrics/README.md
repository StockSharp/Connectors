# StockSharp Coin Metrics Connector

The connector integrates StockSharp with Coin Metrics API v4. It combines the
Community or paid REST API with the paid WebSocket time-series streams and is a
market-data connector only. It does not submit or manage orders.

## Access and configuration

`ApiEndpoint` defaults to the free Community API at
`https://community-api.coinmetrics.io/v4/`. Community access does not require
an API key, but its dataset and rate limits are smaller than the paid product.
To use a paid entitlement, set the REST endpoint to
`https://api.coinmetrics.io/v4/` and configure `Token`.

`SocketEndpoint` defaults to `wss://api.coinmetrics.io/v4/`. Coin Metrics does
not offer WebSocket access on the Community plan, so an API key is required
for every live subscription. API v4 defines `api_key` as a query parameter for
both HTTP pagination URLs and WebSocket handshakes. The connector follows that
protocol requirement, never logs request URLs, redacts the configured key from
transport errors, verifies the key on every returned pagination URL, and
rejects pagination outside the configured HTTPS origin.

`ExchangeFilter` narrows market discovery on the server.
`IsInactiveIncluded` controls offline markets and `IsExperimentalIncluded`
controls markets whose collection is marked experimental. `MaximumItems` caps
security lookup. `RequestInterval` defaults to 650 milliseconds, matching the
Community limit of 10 requests per six seconds with a small safety margin;
paid users can reduce it.

Each security retains Coin Metrics' canonical `market` identifier, such as
`coinbase-btc-usd-spot`, in both its StockSharp code and native identity.
Market reference metadata maps spot, futures, and options, including price and
amount increments, contract size, listing and expiration, strike, and option
side when supplied by Coin Metrics.

## Market data

The connector supports:

- paginated market-reference discovery;
- historical and live market trades;
- historical and live top-of-book quotes as StockSharp Level 1 data;
- historical order-book snapshots and live reconstructed order books;
- historical and live candles for 1m, 5m, 10m, 15m, 30m, 1h, 4h, and 1d.

Historical requests use explicit UTC start and exclusive end timestamps,
ascending pagination, and a single exact market. `HistoryLimit` caps emitted
records, while `HistoryLookback` is used only when a historical request omits
its start time. `MarketDepth` can be set from 1 to the REST limit of 30,000.

Coin Metrics live order books contain `snapshot` and `update` messages. The
stream client resets state on every snapshot, validates sequence continuity,
applies zero-size deletions, and emits only complete depth snapshots to
StockSharp. Depths up to 100 use the bounded stream; larger requested depths
use `full_book` and are trimmed to the subscription limit locally. Other live
streams disable initial backfill to avoid replaying the last historical row.

Coin Metrics timestamps carry nanosecond text precision. They are validated as UTC and deterministically truncated to the 100-nanosecond precision supported by `DateTime`.

## Official documentation

- [Coin Metrics API v4 reference](https://docs.coinmetrics.io/api/v4/)
- [API conventions](https://docs.coinmetrics.io/api)
- [Accessing the API and Community data](https://docs.coinmetrics.io/access-our-data/api)
- [Market data overview](https://docs.coinmetrics.io/market-data)
- [Market candles](https://docs.coinmetrics.io/market-data-timeseries/market-candles)
- [Coin Metrics website](https://coinmetrics.io/)
