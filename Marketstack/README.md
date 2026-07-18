# Marketstack connector for StockSharp

This connector integrates StockSharp with the current Marketstack REST API v2. It provides global equity reference data, realtime price snapshots, and historical end-of-day and intraday candles. Marketstack is a data provider in this API, so the connector does not expose portfolios or order routing.

## Supported functionality

- API-key authentication through the documented `access_key` query parameter.
- Global ticker lookup with provider-side symbol/name search, exchange filtering, pagination, and native identifiers that retain both the exchange MIC and acronym.
- Realtime Level1 last-price snapshots through the dedicated `/stockprice` endpoint.
- Historical end-of-day candles through `/eod`.
- Historical intraday candles at 1, 5, 10, 15, and 30 minutes and 1 hour through `/intraday`.
- Raw or corporate-action-adjusted OHLC values. If Marketstack omits a complete adjusted OHLC set for an observation, the connector uses the complete raw set rather than mixing adjusted and raw prices.
- Optional pre-market and post-market intraday data through the documented `after_hours` parameter.
- Full offset pagination, UTC normalization, duplicate-bar removal, bounded retry handling for rate limits and transient server failures, and typed API errors.

Every Marketstack request and response used by the connector is represented by a concrete typed DTO. The implementation does not use `JObject`, `JArray`, `JToken`, `dynamic`, protocol dictionaries, or `object[]`.

## Configuration

- `Token` is the access key from the Marketstack dashboard.
- `Address` defaults to the official `https://api.marketstack.com/v2/` base address.
- `StockExchange` is optional. Set an exchange MIC such as `XNAS` or `XNYS` to qualify manually entered tickers and filter lookup and candle requests.
- `PriceAdjustment` selects raw or adjusted historical OHLC fields.
- `IsAfterHours` requests pre-market and post-market intraday observations when the account and instrument support them.

Use securities returned by lookup whenever possible. Their StockSharp `Native` identifier preserves the exact Marketstack symbol, exchange MIC, and exchange acronym. If an unqualified manually entered ticker resolves to more than one exchange, the connector reports the ambiguity instead of combining observations from different listings.

## Data semantics and limitations

Marketstack API v2 is REST-only and does not publish a WebSocket protocol. Level1 subscriptions therefore return one current observation and finish; the connector does not imitate streaming with hidden REST polling. Marketstack intraday OHLC bars are candles, not individual trades, and are not exposed as tick data.

Realtime prices, intraday history, after-hours data, exchange coverage, history depth, and request allowance depend on the Marketstack plan. The free tier primarily provides end-of-day data. Authentication, entitlement, and rate-limit failures are propagated to StockSharp instead of being treated as empty data.

The API also publishes splits, dividends, indices, bonds, ETF holdings, commodities, company ratings, and SEC fundamentals. Those products are not forced into unrelated StockSharp Level1 or candle messages. Splits and dividends already represented by adjusted OHLC fields remain available through the provider API but are not mislabeled as market trades.

The `/stockprice` schema describes `trade_last` as the last-trade timestamp. Offset-free values are interpreted as UTC at the protocol boundary. Candle timestamps include their provider timezone and are normalized to UTC.

## Official documentation

- [Marketstack API v2 documentation](https://docs.apilayer.com/marketstack/docs/marketstack-api-v2-v-2-0-0)
- [Marketstack product documentation](https://marketstack.com/documentation)
- [Marketstack plans and feature availability](https://marketstack.com/product)
- [StockSharp Marketstack connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/marketstack.html)

Data provided by Marketstack. Marketstack and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Marketstack.
