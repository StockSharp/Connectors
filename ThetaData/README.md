# ThetaData connector for StockSharp

This connector integrates StockSharp with the current ThetaData v3 REST API and event WebSocket exposed by Theta Terminal. It provides US stock, option, and index reference data, snapshots, historical market data, and genuine realtime events. ThetaData is a market-data provider, so the connector does not expose portfolios or order routing.

## Supported functionality

- Stock and index symbol lookup plus option-contract lookup with OCC-compatible identifiers, expiry, strike, right, multiplier, and underlying security.
- Level1 snapshots and history from stock and option quotes and index price reports.
- Top-of-book market depth snapshots, history, and realtime updates for stocks and options.
- Genuine historical and realtime stock and option trade ticks; index values are kept as Level1 prices and are not mislabeled as exchange trades.
- Historical OHLC candles at 10, 100, and 500 milliseconds; 1, 5, 10, 15, and 30 seconds; 1, 5, 10, 15, and 30 minutes; 1 hour; and end of day.
- One shared, lazily opened WebSocket connection, typed subscribe and unsubscribe acknowledgements, bounded frames, reconnect handling, and subscription restoration.
- Per-day intraday requests, configurable market sessions, US Eastern timestamp interpretation, UTC output, bounded retry handling, and typed API errors.

Every REST response and WebSocket request or event used by the connector is represented by a concrete typed DTO. The implementation does not use `JObject`, `JArray`, `JToken`, `dynamic`, protocol dictionaries, or `object[]`.

## Requirements and configuration

Theta Terminal must be installed, authenticated, running, and entitled to the requested datasets. The terminal owns ThetaData credentials; this C# connector therefore does not ask for an API token.

- `Address` defaults to the terminal's current REST v3 base address, `http://127.0.0.1:25503/v3/`.
- `WebSocketAddress` defaults to `ws://127.0.0.1:25520/v1/events`.
- `StockVenue` selects Nasdaq Basic (`nqb`) or the merged UTP/CTA SIP feed (`utp_cta`) for supported stock REST endpoints.
- `MarketTimeZoneId` defaults to `America/New_York`. On Windows, `Eastern Standard Time` is used as the compatible fallback.
- `SessionStart` and `SessionEnd` bound each intraday history request and default to the US regular session, 09:30–16:00 Eastern.

Use securities returned by lookup whenever possible. Option identifiers preserve the ThetaData root, expiration, strike, and right in StockSharp's `Native` field and expose the standard 21-character OCC symbol when it can be represented exactly.

## Data semantics and limitations

ThetaData REST timestamps do not carry a UTC offset. They are interpreted in the configured US Eastern time zone, including daylight-saving transitions, and converted to UTC. Streaming timestamps combine the documented `date` and `ms_of_day` fields and use the same conversion.

Stock WebSocket events are Nasdaq Basic only. Selecting `UtpCta` uses SIP data for REST snapshots and history, but the connector finishes snapshot/history subscriptions instead of silently switching a requested SIP subscription to the narrower realtime feed. Realtime stock ticks require `NasdaqBasic`. Options stream from OPRA and indices from CBOE CGIF according to the provider's entitlements.

Level1 and market-depth data for stocks and options come from genuine quote reports. Market depth is therefore documented top of book, not a fabricated multi-level order book. Index price reports are exposed as Level1 values. Stock and option ticks come only from trade endpoints and trade events.

Dataset availability depends on the ThetaData plan and exchange agreements. Symbol lists and end-of-day data may be available on lower tiers; quotes, trades, realtime feeds, and options data can require separate Value, Standard, Professional, OPRA, or stock-feed permissions. Status 472 (`NO_DATA`) is treated as an empty result. Authentication, permission, invalid-parameter, oversized-request, and exhausted retry failures are propagated to StockSharp.

Greeks, open interest, bulk flat files, and raw exchange/condition reference tables are not forced into unrelated StockSharp messages. They remain available through ThetaData's native products but are outside the normalized feeds implemented here.

## Official documentation

- [ThetaData documentation](https://docs.thetadata.us/)
- [ThetaData OpenAPI v3 specification](https://docs.thetadata.us/openapiv3.yaml)
- [Theta Terminal getting started](https://docs.thetadata.us/Articles/Getting-Started/Getting-Started.html)
- [Streaming getting started](https://docs.thetadata.us/Articles/Streaming/Getting-Started.html)
- [StockSharp ThetaData connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/thetadata.html)

Data provided by ThetaData. ThetaData and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by ThetaData.
