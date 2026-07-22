# Tiingo connector for StockSharp

This connector integrates StockSharp with Tiingo's official REST and WebSocket APIs. It provides stock and fund reference data, US equity prices, forex, cryptocurrencies, candles, and financial news. Tiingo is a data provider rather than a broker in these APIs, so the connector does not expose portfolios or order routing.

## Supported functionality

- Token authentication through the documented REST `Authorization: Token ...` header and WebSocket authorization field.
- US stock, ETF, and mutual-fund lookup through Tiingo search and the official daily supported-tickers CSV; forex and crypto reference lookup through their native endpoints.
- Native identifiers that retain the Tiingo market family, exact ticker, and optional crypto venue.
- REST Level1 snapshots for equities and forex, plus the latest consolidated crypto reference price.
- Genuine, independently managed WebSocket feeds for IEX equities, forex, and crypto, with bounded frames, reconnect, entitlement errors, and subscription restoration.
- IEX TOPS trade ticks when the account has IEX permission, and real per-exchange crypto trades. Derived equity reference prices and forex midpoints are never emitted as trades.
- Native equity intraday and end-of-day candles, forex candles, and crypto candles at 1, 5, 15, 30, and 45 minutes; 1, 2, 4, and 8 hours; and daily, weekly, and monthly intervals.
- Raw or split- and dividend-adjusted equity end-of-day candles, extended-hours and gap-fill controls for IEX intraday candles, chunked history requests, UTC normalization, and duplicate-boundary removal.
- Historical financial news with ticker, date, count, and pagination filters.

## Configuration

- `Token` is the API token shown in the Tiingo account dashboard.
- `Address` defaults to the official `https://api.tiingo.com/` REST base address.
- `IexWebSocketAddress`, `ForexWebSocketAddress`, and `CryptoWebSocketAddress` default to Tiingo's official market-specific endpoints.
- `SupportedTickersAddress` points to Tiingo's daily supported-tickers CSV used for a full stock and fund lookup.
- `EquityStreamingMode` defaults to `ReferencePrice`, which uses Tiingo's exchange-compliant derived reference price. `IexTop` requests filtered IEX quotes and trades; `IexAll` requests every IEX update. Both IEX modes require the appropriate agreement and entitlement.
- `CryptoExchange` optionally selects one venue's quote and trade stream. When empty, the connector uses Tiingo's documented trade-only crypto firehose and emits genuine trades from all venues under the consolidated symbol.
- `PriceAdjustment` chooses raw or adjusted end-of-day equity candles.
- `IsAfterHours` includes eligible US pre-market and post-market intraday bars.
- `IsForceFill` requests Tiingo's documented intraday gap filling.

Use securities returned by lookup whenever possible. StockSharp's `Native` identifier retains Tiingo's exact symbol identity while `SecurityCode` remains the familiar ticker or pair.

## Data semantics and limitations

Access depends on the Tiingo subscription, market-data agreements, and permitted use. A valid token does not imply access to every endpoint or streaming threshold. Rate-limit, authentication, and entitlement failures are returned to StockSharp instead of being treated as empty data.

The default equity WebSocket threshold emits a Tiingo-derived reference price, which is mapped to StockSharp `TheorPrice`; it is not a last trade. Stock ticks are available only in an IEX TOPS mode. IEX TOPS access has separate exchange requirements. Forex streaming contains bid, ask, and midpoint quotes and therefore does not advertise forex ticks.

The crypto REST order-book endpoint is intentionally not used because Tiingo documents the consolidated top-of-book feed as deprecated. A crypto Level1 snapshot uses the latest consolidated reference price. WebSocket quote updates are enabled only when `CryptoExchange` identifies one venue; otherwise venue-specific books are not mixed into a fabricated consolidated best bid and ask.

WebSockets are opened lazily. The crypto firehose is not connected until the first live crypto subscription and is stopped when the last crypto subscription ends. Equity and forex subscriptions are restored after reconnect.

Tiingo publishes fundamentals, fund fees, dividends, and split endpoints. StockSharp's normalized message model used by this connector has no direct fundamentals transport, so those raw datasets are outside this connector rather than being forced into unrelated messages.

## Official documentation

- [Tiingo API overview and authentication](https://www.tiingo.com/documentation/general/overview)
- [End-of-day stock prices](https://www.tiingo.com/documentation/end-of-day)
- [IEX REST and WebSocket data](https://www.tiingo.com/documentation/iex)
- [Forex REST and WebSocket data](https://www.tiingo.com/documentation/forex)
- [Crypto REST and WebSocket data](https://www.tiingo.com/documentation/crypto)
- [Financial news](https://www.tiingo.com/documentation/news)
- [StockSharp Tiingo connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/tiingo.html)

Data provided by Tiingo. Tiingo and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Tiingo.
