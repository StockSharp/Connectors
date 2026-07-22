# StockSharp Amberdata Connector

The connector integrates StockSharp with Amberdata's normalized spot market
data. It combines the Amberdata REST API with the spot WebSocket JSON-RPC API
and is a market-data connector only. It does not submit or manage orders.

## Access and configuration

An Amberdata API key is required. Both REST and WebSocket connections send it
only in the `x-api-key` header. The key is never placed in a URL or protocol
payload.

`ApiEndpoint` defaults to `https://api.amberdata.com/markets/` and
`SocketEndpoint` defaults to `wss://ws.amberdata.com/spot`. The connector
rejects insecure endpoints, query strings, and fragments. `RequestInterval`
paces REST calls, while transient rate-limit and server responses use bounded
retries.

Security lookup reads Amberdata's paginated spot-exchange reference catalog.
`ExchangeFilter` narrows discovery to one Amberdata exchange identifier,
`IsInactiveIncluded` includes inactive or delisted instruments, and
`MaximumItems` caps the result. Every security has the canonical
`exchange:instrument` native identity and uses the Amberdata board code.

`HistoryLimit` caps records produced by a historical subscription.
`HistoryLookback` supplies the range only when a request has no start time.
`MarketDepth` limits both historical and live order-book snapshots. The REST
client follows Amberdata's opaque pagination links without rebuilding them and
splits ranges to remain within the documented endpoint windows.

## Market data

The connector supports:

- paginated spot-instrument discovery;
- historical and live trades;
- historical and live ticker snapshots as StockSharp Level 1 data;
- historical and live full order-book snapshots;
- historical one-minute, one-hour, and one-day OHLCV candles;
- live one-minute OHLCV candles.

Historical trades, tickers, and OHLCV are downloaded in chunks no longer than
731 days. Order-book history uses conservative 540-day chunks within the
documented 18-month window. Every request has explicit UTC start and end
timestamps, and results are emitted in chronological order.

Live subscriptions share one reconnecting WebSocket connection. The client
uses typed JSON-RPC subscribe and unsubscribe DTOs, validates returned
subscription identifiers, and restores active streams after reconnect. Trade
rows and book levels are positional arrays in Amberdata's wire protocol; they
are parsed by dedicated typed converters that validate field count and value
types. The implementation does not use dynamic JSON trees, anonymous protocol
objects, protocol dictionaries, or untyped object arrays.

Amberdata order streams publish complete side snapshots. The connector keeps
the latest bid and ask sides together and emits a complete StockSharp depth
snapshot after each update. Exact subscription counts are enforced for live
streams; an updating candle is counted once by its opening timestamp.

## Official documentation

- [HTTP API fundamentals](https://docs.amberdata.io/http/http-api-fundamentals)
- [Spot exchange reference](https://docs.amberdata.io/http/market/spot-exchanges-reference)
- [Historical spot trades](https://docs.amberdata.io/http/market/spot-trades)
- [Historical spot tickers](https://docs.amberdata.io/http/market/spot-tickers)
- [Historical order-book snapshots](https://docs.amberdata.io/http/market/spot-order-book-snapshots)
- [Historical OHLCV](https://docs.amberdata.io/http/market/spot-ohlcv)
- [WebSocket getting started](https://docs.amberdata.io/real-time/websocket-getting-started)
- [Spot WebSocket channels](https://docs.amberdata.io/real-time/market/websocket-market-spot)
- [Advanced WebSocket usage](https://docs.amberdata.io/real-time/websocket-advanced)
- [Amberdata website](https://www.amberdata.io/)
