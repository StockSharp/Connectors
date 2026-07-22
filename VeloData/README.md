# StockSharp Velo Data Connector

The connector integrates StockSharp with the current Velo Data HTTP market
data API and News API. It is a read-only analytics connector and does not
submit orders. Market rows are historical aggregates with a minimum resolution
of one minute; only the News API has an official real-time WebSocket.

## Access and authentication

Set `Token` to a Velo Data API key. The connector sends it as documented HTTP
Basic authentication with the fixed user `api`. Credentials are placed in the
`Authorization` header and never in URLs. The defaults point to the production
services:

- `https://api.velo.xyz/api/v1/` for market data;
- `https://api.velo.xyz/api/n/` for news history;
- `wss://api.velo.xyz/api/w/connect` for live news.

At connection time the adapter loads the active futures, spot, and options
catalogues. `IsIncludeDelisted` additionally loads delisted futures and spot
products. The Velo exchange, product, market kind, coin, and first available
timestamp are preserved in a typed native security identity, so identical
symbols from different venues cannot collide.

## Market data

The connector supports security lookup, historical Level 1 values, and
finished time-frame candles at 1, 5, 10, 15, and 30 minutes; 1 and 4 hours;
and 1 and 7 days. Spot and futures candles use the documented price OHLC,
coin volume, and total-trade columns. Futures Level 1 history also maps coin
open interest. Velo options rows are aggregate analytics rather than tradable
option contracts, so they are exposed as index securities: DVOL OHLC becomes
their candle series and `index_price` remains the Level 1 index value.

The `/rows` limit of 22,500 values is enforced by splitting each request using
the exact number of selected columns. Batches are deduplicated by UTC timestamp.
`RequestInterval` defaults to 250 milliseconds, matching the documented limit
of 120 requests per 30 seconds. Responses and CSV lines have hard size bounds,
CSV headers are validated, and numeric values are parsed with invariant culture.
`HistoryLimit` caps emitted records and `HistoryLookback` supplies a bounded
range when no start time is provided.

Funding rates, premiums, liquidations, option greeks, and dollar-volume fields
are not relabeled as unrelated StockSharp fields. Velo depth data is also left
out: its price levels are dynamic CSV columns and the main API does not provide
a documented live order-book WebSocket.

## News

Historical news is loaded from `/news?begin=...`. Live subscriptions use the
official WebSocket with the `subscribe news_priority` command. A security-bound
subscription filters stories by the instrument coin; an empty security receives
all stories. Publish time, identifier, headline, Markdown summary, source, and
source link map directly to `NewsMessage`. Edit events retain their identifier;
delete events are ignored because StockSharp news messages have no exact delete
operation. Server heartbeat and lifecycle signals are handled by the transport,
which reconnects according to the adapter reconnection settings.

Every JSON payload uses a concrete DTO. CSV rows are converted immediately into
typed catalogue or market-row DTOs; the connector contains no dynamic JSON
trees, protocol dictionaries, anonymous protocol objects, or untyped object
arrays.

## Official documentation

- [Velo Data API](https://docs.velo.xyz/api)
- [HTTP API](https://docs.velo.xyz/api/http)
- [Market-data columns](https://docs.velo.xyz/api/columns)
- [News API](https://docs.velo.xyz/api/news-api)
- [Node.js client and request batching](https://github.com/velodataorg/velo-node)
- [Velo website and subscriptions](https://velo.xyz/subscription)
