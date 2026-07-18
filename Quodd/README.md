# QUODD connector for StockSharp

This connector integrates the public QUODD Developer Platform directly. Current quotes use the official unary Snap gRPC method, while live equities and options use the official server-streaming `GetSnapsStream` method at `api.quodd.com:443`. QUODD publishes gRPC streaming for this API; the connector does not label it as WebSocket or invent a WebSocket endpoint.

## Supported functionality

- JWT authentication with an already issued token.
- Automatic 24-hour JWT acquisition and renewal for trial user credentials.
- Automatic JWT acquisition and renewal for a user backed by separate firm HTTP Basic credentials.
- Exact equity security lookup through Snap, optionally enriched by the separately entitled Ticker Info service.
- Option-chain lookup by underlying, expiration and call/put type through the official Option Lookup service.
- Exact option lookup through the Options Snap service.
- Current and streaming Level1 data for equities and options: NBBO bid/ask, last trade, daily OHLC, previous close, volume, trade count, VWAP, 52-week range, percentage change and trading state.
- Shared streaming sessions for all active equity tickers and all active option tickers.
- Automatic stream reconstruction when the ticker set changes and automatic reconnect after transport failure or QUODD's documented 30-minute inactive-stream termination.
- Rate-limit and transient gRPC retry handling with a capped backoff and a longer cool-off after the configured retry cycle.

The checked-in Snap, Ticker Info and Option Lookup contracts are the concrete `.proto` files published by QUODD, with only C# namespace options added for package isolation. Authentication responses and errors use dedicated DTOs. The implementation does not use `JObject`, `JArray`, `JToken`, `dynamic`, anonymous wire objects, protocol dictionaries or `object[]`.

## Configuration

Choose `AuthenticationMode`:

- `Token` - set `Token` to a valid QUODD JWT.
- `Trial` - set `Login` and `Password`; the connector calls `/vor/quodd/login/trial/token` and renews the returned JWT before its documented 24-hour lifetime ends.
- `Firm` - set `Login` to the QUODD user name and set `FirmLogin` and `FirmPassword` to the credentials used in the HTTP Basic authorization header for `/vor/quodd/api/login/token`.

`Address` defaults to `https://api.quodd.com`, and `AuthenticationAddress` defaults to `https://vor.quodd.com`. Both must remain HTTPS endpoints. `ValidationTicker` defaults to `MSFT` and is requested once during connection to validate the JWT and streaming entitlement; clear it only when connection-time validation is undesirable.

Ticker Info is a separate QUODD subscription. `IsTickerInfoEnabled` controls whether security lookup requests it for the instrument name, shares outstanding and sector fallback metadata. Disable the setting when the account has Snap entitlement but no Ticker Info entitlement.

Use board `QUODD` for equities and `QUODD_OPT` for options. QUODD option-chain lookup is selected by the options board, option security type, underlying identifier or option filters. An OCC-style ticker containing digits is treated as an exact option symbol.

## Data and API boundaries

Snap is current and streaming market data, not tick-by-tick trade history. A history-only Level1 request without a date range returns the current snapshot; dated Level1 history is rejected. The public Developer Platform contract used here does not expose market depth, order log, candles, portfolios, orders or executions, so the connector does not advertise those capabilities.

The feed returns quote and trade timestamps as US Eastern market time. They are converted to UTC with daylight-saving rules before StockSharp messages are emitted. Exchange entitlements, delayed versus real-time status, symbol coverage, redistribution rights and access to Ticker Info or Option Lookup remain subject to the customer's QUODD agreement.

## Official documentation

- [QUODD Developer Platform](https://developer.quodd.com/)
- [Snap gRPC API and streaming contract](https://developer.quodd.com/docs/snap-grpc-api/)
- [Ticker Info gRPC API](https://developer.quodd.com/docs/ticker-info-grpc-api/)
- [Option Lookup gRPC API](https://developer.quodd.com/docs/option-lookup-grpc-api/)
- [Trial JWT endpoint](https://developer.quodd.com/docs/rest-api/post-token-for-trial-user/)
- [Firm JWT endpoint](https://developer.quodd.com/docs/rest-api/post-token-for-firm-user/)
- [QUODD stock and ETF data API](https://www.quodd.com/stock-and-etf-data)
- [StockSharp QUODD connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/quodd.html)

QUODD and its marks are trademarks of QUODD Financial Information Services, Inc. StockSharp is not affiliated with or endorsed by QUODD.
