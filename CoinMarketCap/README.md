# StockSharp CoinMarketCap Connector

The connector integrates StockSharp with the current CoinMarketCap Pro REST
API and the official CoinMarketCap WebSocket beta. It exposes CoinMarketCap's
aggregated cryptocurrency reference prices and historical OHLCV; it is a data
connector, not an execution venue.

## Access and configuration

`AccessMode` selects one of two REST roots:

- `Keyless` uses `https://pro-api.coinmarketcap.com/public-api` without a key;
- `ApiKey` uses `https://pro-api.coinmarketcap.com` and sends the key only in
  the `X-CMC_PRO_API_KEY` header.

Keyless mode supports security discovery and current Level 1 snapshots. Its
limits are shared by source IP and are intended for evaluation and light use.
An API key provides account-specific limits and the larger Pro endpoint
catalog. `RequestInterval` adds client-side pacing and defaults to two seconds.

`QuoteCurrency` defaults to `USD` and controls REST conversion. Security
identity retains the numeric CoinMarketCap cryptocurrency ID as well as the
selected quote currency, so assets with duplicate symbols remain distinct.

The WebSocket beta requires an API key on Startup plan or above. It connects
to `wss://pro-stream.coinmarketcap.com/v1`, authenticates by header, follows
the server's welcome-message ping interval, restores subscriptions after a
reconnect, and shares an upstream subscription between identical StockSharp
requests. The market stream is USD-denominated, so live Level 1 requests must
use `USD`; non-USD currencies remain available for REST snapshots. Disable
`IsStreamingEnabled` for REST-only use.

## Market data

The connector supports:

- active cryptocurrency discovery through `/v1/cryptocurrency/map`, paged in
  batches of at most 5,000;
- current aggregated price, 24-hour volume, and 24-hour percentage change
  through `/v3/cryptocurrency/quotes/latest`;
- live aggregated USD price updates through the
  `market@crypto_latest_price` WebSocket channel;
- historical hourly and daily OHLCV through
  `/v2/cryptocurrency/ohlcv/historical` on Startup plan or above.

CoinMarketCap currently documents only native hourly and daily periods for the
CEX historical OHLCV endpoint. The connector therefore exposes exactly 1h and
1d candles and does not fabricate intermediate intervals. Historical requests
are limited to 10,000 candles. Hourly volume before 2020-09-22 can be absent;
such candles are emitted with zero StockSharp volume while prices remain
intact.

CoinMarketCap's aggregated latest price is reference data, not an individual
exchange trade. The connector deliberately does not synthesize tick trades,
order books, executable quotes, orders, portfolios, or withdrawals from it.
CoinMarketCap also offers separate on-chain DEX endpoints and WebSocket
channels; those pool-specific feeds are outside this aggregated-asset
connector's scope.

API timestamps are normalized to UTC `DateTime` values.

## Official documentation

- [CoinMarketCap API documentation](https://coinmarketcap.com/api/documentation/)
- [Keyless Public API](https://coinmarketcap.com/api/documentation/pro-api-reference/keyless-public-api)
- [Cryptocurrency REST API](https://coinmarketcap.com/api/documentation/pro-api-reference/cryptocurrency)
- [WebSocket beta overview](https://coinmarketcap.com/api/documentation/pro-api-websocket/overview)
- [Cryptocurrency latest-price stream](https://coinmarketcap.com/api/documentation/pro-api-websocket/cryptocurrency)
- [API changelog](https://coinmarketcap.com/api/documentation/changelog)
- [CoinMarketCap terms](https://coinmarketcap.com/terms/)
