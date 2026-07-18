# Benzinga connector for StockSharp

This connector integrates the currently documented Benzinga REST APIs and the official real-time News WebSocket. It uses concrete request and response DTOs throughout; the symbol-keyed delayed-quote response is read by a typed streaming JSON converter without `JObject`, `JArray`, `JToken`, `dynamic`, or protocol dictionaries.

## Supported functionality

- Exact security lookup by stock symbol or ISIN through Delayed Quotes v2.
- A finite Level1 snapshot from the 15-minute delayed quote product, including bid/ask, last price, close, previous close, volume, and change when supplied.
- Historical OHLCV candles at 1, 5, 15, and 30 minutes, 1 hour, 1 day, and 1 week.
- Benzinga trading-session selection: all, pre-market, regular, or after-market.
- Historical full-text news through News API v2.
- Real-time news through `wss://api.benzinga.com/api/v1/news/stream`.
- Optional local and server-side filtering by Benzinga news channels.
- WebSocket heartbeat, bounded message deduplication, reconnect with backoff, and restoration of active StockSharp subscriptions after reconnect.

## Authentication and entitlements

Create an API key in the Benzinga Developer Console and set `Token`. REST calls use the production-recommended `Authorization: token ...` header so the key does not enter HTTP URL logs. Benzinga requires the key in the WebSocket query string for the news handshake; the connector never logs that authenticated URI.

Benzinga licenses products separately. A valid key can therefore be entitled to news but not delayed quotes or bars, or vice versa. The adapter does not reject a usable key merely because an unrelated product returns `403`; each requested operation reports its own entitlement error.

`Address` defaults to `https://api.benzinga.com/`. `WebSocketAddress` defaults to the official News WebSocket. `NewsChannels` accepts the provider's comma-separated channel names. `MaxNewsItems` and `MaxBars` bound large REST responses locally.

## Data semantics and boundaries

The Delayed Quotes API is documented as 15-minute delayed and is a REST snapshot, not a live quote stream. Level1 requests therefore emit at most one snapshot and finish. The public WebSocket reference documents live content streams but does not publish the wire contract for the separate `/ws/data` market-feed product; this connector does not guess that contract or advertise live trades or quote updates.

The Bars API is historical and finite. Monthly bars are excluded because StockSharp time-frame candles use a fixed `TimeSpan`, while calendar months do not. If a candle request omits `From`, the adapter requests a bounded lookback and returns the most recent requested candles in chronological order.

Security lookup is exact because the current delayed-quote endpoint accepts symbols, ISINs, or CIKs rather than a searchable security master. Empty and wildcard lookups return no securities. News updates reuse the Benzinga article ID. Deleted WebSocket events are ignored because `NewsMessage` has no deletion semantic; consumers receive created and updated article versions.

The News WebSocket allows one connection per API token. A single unfiltered connection is shared by all active StockSharp news subscriptions and ticker filtering is performed locally. The configured channel filter is also included in the WebSocket handshake. The client sends the documented plain-text `ping` heartbeat and handles reconnection after unexpected closes.

## Official documentation

- [Benzinga API documentation](https://docs.benzinga.com/)
- [REST authentication](https://docs.benzinga.com/api-reference/authentication)
- [Delayed Quotes v2](https://docs.benzinga.com/api-reference/quotedelayed/get-delayed-quotes)
- [Historical Bars](https://docs.benzinga.com/api-reference/bars/get-bars)
- [News API v2](https://docs.benzinga.com/api-reference/news-api/get-news-items)
- [News WebSocket](https://docs.benzinga.com/ws-reference/data-websocket/get-news-stream)
- [WebSocket overview](https://docs.benzinga.com/ws-reference/overview)
- [WebSocket actions and replay](https://docs.benzinga.com/ws-reference/actions)
- [StockSharp Benzinga connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/benzinga.html)

Benzinga and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Benzinga.
