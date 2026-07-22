# EOD Historical Data connector for StockSharp

This connector integrates StockSharp with EOD Historical Data (EODHD) through the provider's official REST and WebSocket APIs. It covers global exchange reference data, end-of-day and intraday prices, US equity ticks, US options, financial news, forex, and cryptocurrencies. EODHD is a data provider in these APIs, so the connector does not expose portfolios or order routing.

## Supported functionality

- API-token authentication through the documented `api_token` query parameter.
- Security lookup through global search and exchange symbol lists, including the configured stock exchange, forex, crypto, and filtered US option-contract lookup.
- Native identifiers that retain the exact EODHD market family, symbol, exchange code, and option underlying.
- Delayed REST Level1 snapshots for exchange securities, forex, and crypto.
- Historical and latest option Level1 records with actual bid, ask, last trade, volume, open interest, implied volatility, theoretical price, and Greeks when those fields are present in the entitled Marketplace response.
- Genuine WebSocket streams for US equity trades and quotes, forex quotes, and crypto trades. Each product is opened lazily, reconnects with bounded frames, and restores its own subscriptions.
- Historical US equity tick trades through the official column-oriented tick endpoint, plus live US equity and crypto trades.
- Intraday candles at 1, 5, 15, and 30 minutes and 1 hour; end-of-day candles at daily, weekly, and monthly intervals; and daily option candles.
- Historical financial news with optional symbol and date filters, count limits, pagination, UTC normalization, and duplicate-link removal.
- Explicit handling of provider limits, authentication failures, entitlement errors, and malformed column-oriented tick responses.

## Configuration

- `Token` is the API token from the EODHD user dashboard.
- `Address` defaults to the official `https://eodhd.com/api/` REST base address.
- `StockTradeWebSocketAddress` and `StockQuoteWebSocketAddress` are the separate official US trade and quote products.
- `ForexWebSocketAddress` and `CryptoWebSocketAddress` are the official market-specific streaming products.
- `StockExchange` defaults to `US`. It qualifies stock identifiers that do not already include an EODHD exchange suffix and selects the exchange used for an unfiltered stock lookup.
- `IsDelisted` includes delisted instruments when an exchange symbol list is requested.
- `MaxWebSocketSymbols` defaults to the provider's documented 50-symbol allowance per streaming product. Accounts with a different allowance can set their actual limit here.

Use securities returned by lookup whenever possible. StockSharp's `Native` identifier preserves the provider's exact symbol and exchange while `SecurityCode` remains the familiar ticker, currency pair, crypto pair, or option contract.

## Data semantics and limitations

Endpoint access depends on the EODHD subscription and separate Marketplace entitlements. The free plan is limited, while WebSocket, historical tick, and US option products can require paid or additional access. Authentication, rate-limit, and entitlement failures are returned to StockSharp instead of being treated as empty data.

US equities have separate trade and top-of-book WebSockets. A Level1 subscription combines only those genuine updates; a tick subscription uses the trade product. The forex stream contains bid and ask quotes and is never advertised as trades. The crypto stream contains genuine price-and-quantity updates. EODHD does not publish equivalent WebSocket products for arbitrary non-US exchange securities or for options, so the connector finishes those Level1 subscriptions after REST data instead of simulating realtime polling.

The historical tick endpoint is documented for US stocks and returns parallel arrays. The connector validates the required price and timestamp columns before emitting trades. The API does not document cursor pagination for this endpoint, so one bounded request is made rather than inventing an unsafe paging scheme.

EODHD end-of-day responses contain raw OHLC and a separate adjusted close. The connector emits coherent raw OHLC candles. It intentionally does not combine adjusted close with unadjusted open, high, and low values.

US option contracts and EOD option analytics are separate Marketplace products. Option lookup is performed only for an explicit option, underlying, or exact-contract request; the connector does not attempt to download the entire option universe. The non-compact JSON representation is requested explicitly.

EODHD also publishes extensive fundamentals and specialized analytics. StockSharp's normalized message model used by this connector has no direct fundamentals transport, so those raw datasets are not forced into unrelated market-data messages.

## Official documentation

- [EODHD Financial APIs](https://eodhd.com/financial-apis/)
- [Exchange and symbol-list API](https://eodhd.com/financial-apis/exchanges-api-list-of-tickers-and-trading-hours)
- [End-of-day historical data](https://eodhd.com/financial-apis/api-for-historical-data-and-volumes)
- [Intraday historical data](https://eodhd.com/financial-apis/intraday-historical-data-api)
- [US stock tick data](https://eodhd.com/financial-apis/us-stock-market-tick-data-api)
- [Realtime WebSocket products](https://eodhd.com/financial-apis/new-real-time-data-api-websockets)
- [Financial news API](https://eodhd.com/financial-apis/stock-market-financial-news-api)
- [API limits](https://eodhd.com/financial-apis/api-limits)
- [StockSharp EOD Historical Data connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/eodhd.html)

Data provided by EODHD. EODHD and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by EODHD.
