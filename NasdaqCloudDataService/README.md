# Nasdaq Cloud Data Service connector for StockSharp

This connector provides native .NET access to the official Nasdaq Cloud Data Service (NCDS) REST API for real-time or 15-minute delayed exchange data. It uses the customer-specific base URL and OAuth 2.0 credentials supplied during Nasdaq onboarding. It does not scrape Nasdaq web pages or substitute an unrelated retail-data API.

## Supported functionality

- OAuth 2.0 client authentication at `v1/auth/token`, bearer-token caching, and automatic renewal.
- Nasdaq, Nasdaq Texas (`BX` API source), Nasdaq PSX, and consolidated quotes and trades (`CQT`) equity sources.
- Real-time or delayed data offset as assigned in the Nasdaq onboarding email.
- Equity, index, ETP, and option reference lookup with distinct StockSharp boards so instruments with similar symbols retain their API semantics.
- Equity daily snapshots, eligible last sales, and top-of-book quotes as Level1 changes.
- Nasdaq index values and summary snapshots.
- ETP intraday portfolio values and summary snapshots; the connector resolves the trading symbol to its IPV symbol through the reference endpoint.
- US option price snapshots with last trade, NBBO, daily volume, and open interest.
- Optional Nasdaq Options Greeks, implied volatility, and theoretical price.
- Native equity bars for the precisions and ranges published by Nasdaq: 1, 5, 10, 15, and 30 minutes; 1 day; 1 week; and 1 month where supported by the selected source and range.
- Retry handling for rate limits and transient server failures. Individually unlicensed snapshot components can be skipped while other entitled Level1 components are preserved.

Authentication, reference data, equity data, index/ETP data, options, errors, and bars use dedicated protocol DTOs. The symbol-keyed version 2 bars response and the historically inconsistent single-object/array option-contract response use typed streaming converters. The connector does not use `JObject`, `JArray`, `JToken`, `dynamic`, anonymous wire objects, or dictionary-shaped protocol models.

## Configuration

- `Login` is the `client_id` supplied by Nasdaq.
- `Password` is the corresponding `client_secret`.
- `Address` is the customer-specific `base_URL` from the onboarding email. Nasdaq does not publish one universal production host, so the connector deliberately has no fabricated default address.
- `Source` selects `Nasdaq`, Nasdaq Texas (`BX`), `PSX`, or `CQT` for equities.
- `Offset` selects `Realtime` or `Delayed`. It must match the entitlement stated during onboarding.
- `IsOptionGreeksEnabled` additionally calls the separately entitled Nasdaq Options Greeks endpoint. It is off by default so a price-only options subscription does not require the analytics product.

Reference securities use these boards:

- `NCDS` for equities;
- `NCDSIDX` for indexes;
- `NCDSETP` for exchange-traded products;
- `NCDSOPT` for OSI options.

Use securities returned by lookup whenever possible. Option native IDs retain Nasdaq's six-character padded OSI root while their StockSharp security codes use the compact OSI form.

## Data semantics and limitations

NCDS REST endpoints return current real-time or delayed snapshots. A StockSharp Level1 subscription therefore emits the entitled snapshot components and finishes; it is not a continuous subscription. Historical Level1, market depth, tick history, orders, accounts, and portfolios are not advertised.

Nasdaq also offers a separate Streaming API through Java and Python SDKs backed by an onboarded Kafka bootstrap service. It is not a WebSocket API. This .NET connector implements the documented REST contract and does not pretend that repeated REST polling is a WebSocket or Kafka stream.

Equity bars are not synthesized. Nasdaq/CQT version 2 bars accept fixed combinations of precision and relative range, while Nasdaq Texas/PSX version 1 bars accept Eastern-time boundaries and retain up to five days. The connector selects only a documented combination, converts returned Eastern timestamps to UTC, filters the requested interval, and rejects a request that the server cannot represent. Corporate-action adjustment is currently requested as `false`, the only value documented by Nasdaq.

Index and ETP calculated values are represented as `ClosePrice`, not as exchange trades. Option analytics are emitted only from the official Greeks response. Missing or empty timestamps are not replaced with local receipt time.

The REST service is commercial and entitlement-controlled. Product coverage, real-time permission, redistribution rights, data retention, and commercial fees are governed by the customer's Nasdaq agreement. The published REST limit is 100 requests per second, with per-endpoint symbol limits; this connector requests one subscription symbol at a time.

## Official documentation

- [Nasdaq API for real-time or delayed data](https://docs.data.nasdaq.com/docs/api-for-real-time-or-delayed-data)
- [Official REST API reference and examples](https://github.com/Nasdaq/NasdaqCloudDataService-REST-API)
- [REST API rate limits](https://docs.data.nasdaq.com/docs/rate-limits-for-real-timedelayed-rest-api)
- [Nasdaq Streaming API](https://docs.data.nasdaq.com/docs/streaming-api)
- [Official Python streaming SDK](https://github.com/Nasdaq/NasdaqCloudDataService-SDK-Python)
- [Official Java streaming SDK](https://github.com/Nasdaq/NasdaqCloudDataService-SDK-Java)
- [StockSharp Nasdaq Cloud Data Service connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/nasdaq_cloud_data_service.html)

Nasdaq and Nasdaq Cloud Data Service are trademarks of Nasdaq, Inc. StockSharp is not affiliated with or endorsed by Nasdaq.
