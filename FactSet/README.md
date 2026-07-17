# FactSet Prices connector for StockSharp

This connector provides native .NET access to the published FactSet Prices API. Its contracts follow the current official OpenAPI specification and Enterprise SDK directly; no Python process or generated SDK runtime is embedded.

## Supported functionality

- Official API-key authentication using `USERNAME-SERIAL` and API key over HTTP Basic authentication.
- Preferred FactSet OAuth 2.0 confidential-client flow using the portal-issued application configuration and RSA JWK.
- OpenID discovery, RS256 client assertions, access-token caching, and automatic token renewal.
- Exact security reference lookup by market ticker, SEDOL, ISIN, CUSIP, or FactSet permanent identifier.
- Name, FactSet identifier, security type, currency, country, primary exchange, local index, and coverage dates.
- Latest and historical equity/fund Level1 values from native end-of-day open, high, low, close, and volume records.
- Native daily OHLCV candles with split, spinoff, dividend, or no adjustment.
- Latest and historical fixed-income bid, mid, and ask observations for supported corporate, government, agency, and municipal bonds.
- Retry handling for rate limits and transient server failures.

Every credential file, OpenID response, JWT header and payload, token form, reference query, price query, response, error, security reference, equity price, and fixed-income price has a dedicated DTO. The connector does not use `JObject`, `JArray`, `JToken`, `dynamic`, anonymous wire objects, or dictionary-shaped protocol models.

## Configuration

Choose one authentication mode:

- `ApiKey` - set `Login` to the FactSet `USERNAME-SERIAL` and `Password` to the API key created in Developer Portal.
- `OAuth` - set `OAuthConfigFile` to the unmodified confidential application JSON downloaded from Developer Portal. The connector reads its client ID and private RSA JWK, discovers the authorization server, signs a short-lived client assertion, and never persists an access token.

Optional market-data settings:

- `Currency` - ISO currency override. Empty retains each security's local currency.
- `PriceAdjustment` - `Split` (FactSet default), `Spinoff`, `Dividend`, or `Unadjusted`.

## Data model and limitations

FactSet Prices accepts known identifiers but is not a free-text instrument search service. StockSharp security lookup therefore resolves the supplied `SecurityCode` exactly; an empty lookup does not pretend to enumerate FactSet's licensed global universe. Returned `requestId` remains the StockSharp code, while the stable `fsymId` is cached as an alias.

Equity/fund history uses `/factset-prices/v1/prices` at native daily frequency with the `LOCAL` trading calendar. The `price` field is exposed as `ClosePrice`, not as a fabricated trade. Fixed-income history uses `/factset-prices/v1/fixed-income` and remains Level1 bid/mid/ask data; OHLC candles are not synthesized from it.

The public Prices contract is end-of-day REST, not a real-time streaming feed, and it has no order lifecycle. The connector consequently does not advertise WebSocket, ticks, market depth, trading, portfolios, or executions. Other FactSet products, including real-time feeds and trading workflows, have separate entitlements and contracts.

Coverage, history, adjustments, currency conversion, redistribution rights, and access to each endpoint depend on the customer's FactSet agreement. The published Prices API rate limit is 25 requests per second.

## Official documentation

- [FactSet Developer Portal](https://developer.factset.com/)
- [FactSet Prices API](https://developer.factset.com/api-catalog/factset-prices-api)
- [FactSet Enterprise SDK](https://github.com/factset/enterprise-sdk)
- [Official .NET Prices SDK contract](https://github.com/factset/enterprise-sdk/tree/main/code/dotnet/FactSetPrices/v1)
- [FactSet OAuth utility and application configuration](https://github.com/factset/enterprise-sdk-utils-dotnet)
- [StockSharp FactSet connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/factset.html)

FactSet and its marks are trademarks of FactSet Research Systems Inc. StockSharp is not affiliated with or endorsed by FactSet.
