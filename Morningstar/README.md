# Morningstar Direct Web Services connector for StockSharp

This connector provides native .NET access to the published Morningstar Direct Web Services contracts. It follows the current official Authentication API, Universe API, and synchronous Time Series API specifications directly; it does not launch Python or depend on an unofficial web scraper.

## Supported functionality

- OAuth 2.0 authentication with the account username and password over HTTP Basic authentication at `/token/oauth`.
- Regional Americas, APAC, and EMEA API endpoints, access-token caching, and automatic renewal after expiry or an HTTP 401 response.
- Entitlement-aware security discovery through the Universe API for either equities or managed investments.
- Optional custom universe and equity exchange/MIC filtering.
- Opaque continuation-token pagination over the complete entitled universe.
- Morningstar Performance ID, Security ID, MSID, ISIN, CUSIP, and trading-symbol requests.
- Security names, types, MIC/exchange codes, currencies, ISIN, CUSIP, SEDOL, and stable Performance IDs when provided by the account's universe.
- Latest and historical Level1 open, high, low, close, and volume values from the native daily OHLCV data point.
- Native daily OHLCV candles and optional output-currency conversion supported by Morningstar.
- Retry handling for rate limits and transient server failures.

## Configuration

- `Login` and `Password` are the Direct Web Services account credentials supplied during Morningstar onboarding.
- `Region` selects the official Americas, APAC, or EMEA base URL. Credentials and entitlements must belong to that region.
- `InvestmentSource` selects `Equities` or `ManagedInvestments` for security lookup.
- `Universe` optionally limits lookup to a custom universe configured for the account.
- `IdentifierType` controls direct time-series lookup. `Auto` recognizes Performance IDs, ISINs, and CUSIPs and otherwise treats the code as a trading symbol. Select an explicit type for Morningstar Security IDs or MSIDs.
- `Currency` is `BASE` by default. Set a supported ISO currency when the entitlement permits converted time-series values.

Use a security returned by StockSharp lookup whenever possible. Its Morningstar Performance ID is stored in `SecurityId.Native`, which removes trading-symbol ambiguity in later requests. For a direct trading-symbol request, specify the exchange MIC as `BoardCode`; the connector sends the documented `EX$$$$<MIC>` exchange filter.

## Data model and limitations

The Universe API returns only investments entitled to the authenticated account. An empty security lookup can therefore enumerate the selected entitled universe, while a code lookup scans the same paginated universe for an exact identifier match. It is not a public free-text search service.

The synchronous `daily-ohlcv` endpoint is end-of-day REST. The `dailyClosingPrice` field is exposed as `ClosePrice`, not as a fabricated last trade. Missing OHLC fields are retained in Level1 data but excluded from candles.

Morningstar Direct Web Services does not expose realtime WebSocket quotes or order lifecycle operations through these published contracts. This connector consequently does not advertise ticks, market depth, live streaming, trading, portfolios, positions, orders, or executions. Data packages, instruments, history depth, currencies, and redistribution rights depend on the customer's Morningstar agreement.

## Official documentation

- [Morningstar for Developers](https://developer.morningstar.com/)
- [Authentication](https://developer.morningstar.com/direct-web-services/documentation/documentation/get-started/authentication)
- [Universe API](https://developer.morningstar.com/direct-web-services/documentation/api-utilities/universe-api/api-reference)
- [Time Series Sync OpenAPI specification](https://developer.morningstar.com/direct-web-services/documentation/direct-web-services/time-series---sync/openapi-specification)
- [Time Series data dictionary](https://developer.morningstar.com/content/hidden-from-navigation/TimeSeriesDataDictionary.xlsx)
- [StockSharp Morningstar connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/morningstar.html)

Morningstar and its marks are trademarks of Morningstar, Inc. StockSharp is not affiliated with or endorsed by Morningstar.
