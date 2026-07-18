# ACTIV Financial connector for StockSharp

This connector uses the official ACTIV Financial AtlasFeed Web One API client. ACTIV publishes One API as the `@activfinancial/one-api` JavaScript/WASM package rather than a public .NET library or a documented wire protocol. The connector therefore starts a local typed Node.js bridge and never invents or reverse-engineers the private gateway protocol.

## Supported functionality

- Security discovery through official One API query snapshots.
- Canonical Level1 snapshots followed by official real-time subscriptions.
- Live trades, corrections, cancellations, and non-regular trade events.
- Time Series Server (TSS) trade history.
- TSS intraday candles at 1, 2, 5, 10, 15, 30, and 60 minutes.
- TSS daily and weekly candles.
- ACTIV real-time, delayed, low-latency, feed-handler, and entitled TREP data sources.
- Native, ACTIV, and TREP symbologies.

The C# protocol boundary consists only of concrete request, response, record, timestamp, and error DTOs. It does not use `JObject`, `JArray`, `JToken`, `dynamic`, protocol dictionaries, or `object[]`.

## Gateway setup

Install a current Node.js release and install the official client once in the gateway directory copied beside the connector assembly:

```text
cd ActivFinancialGateway
npm install --omit=dev
```

The supplied `package.json` pins `@activfinancial/one-api` to version `1.1.4`. Set `GatewayDirectory` if the gateway is installed elsewhere and set `NodePath` if `node` is not on `PATH`. Credentials are sent to the child process through its redirected standard input; they are not placed in command-line arguments or environment variables.

Set `Host` to the One API gateway assigned to the account. The default `aop-ny4-replay.activfinancial.com` is the replay host used in the official tutorials and still requires valid ACTIV credentials and entitlements. Select the matching `DataSource` and `Symbology` for the account.

## Time and history semantics

ACTIV TSS publishes record timestamps in the exchange time zone. The bridge preserves the provider's date/time components and sub-millisecond precision instead of letting the local machine time zone reinterpret them. The adapter converts those components to UTC using `FID_OLSON_TIME_ZONE` when it is present. `FallbackTimeZoneId` (default `UTC`) is used only when the topic does not publish Olson metadata; use separate adapter instances when entitled topics without metadata span different time zones.

TSS is separately permissioned and tick/intraday retention depends on the ACTIV environment. `MaxHistoryResults` protects the local IPC channel from unbounded range responses and defaults to 10,000 records. Requests with a smaller StockSharp count use that smaller limit. Live candles are not synthesized; candle requests are finite TSS history requests and finish after the available result set is emitted.

Security lookup uses ACTIV tag-expression query snapshots and defaults to a trailing symbol match. An empty lookup uses `symbol=*`; `MaxLookupResults` defaults to 1,000 to avoid accidentally materializing the provider's global universe. Use a security returned by lookup when a symbol requires a non-default data source, symbology, MIC, or exchange time zone.

## Product boundary

One API is a market-data API. This connector does not claim order routing, portfolios, balances, or transactions. Market depth is also not advertised: ACTIV depth topics are map topics and require book-state handling by map key and update type, which is deliberately not approximated as flat Level1 data. The official client does not reconnect a broken session automatically; reconnect the StockSharp adapter to establish a new session and restore subscriptions.

## Official documentation

- [ACTIV Web One API](https://weboneapi.activfinancial.com/)
- [One API documentation](https://weboneapi.activfinancial.com/documentation)
- [Getting started](https://weboneapi.activfinancial.com/tutorials/)
- [Queries](https://weboneapi.activfinancial.com/tutorials/queries/)
- [Snapshots](https://weboneapi.activfinancial.com/tutorials/snapshots/)
- [Subscriptions](https://weboneapi.activfinancial.com/tutorials/subscriptions/)
- [Time Series](https://weboneapi.activfinancial.com/tutorials/time-series/)
- [Field Data](https://weboneapi.activfinancial.com/tutorials/fieldData/)
- [Official One API examples](https://github.com/activfinancial/one-api-examples)
- [Official npm package](https://www.npmjs.com/package/@activfinancial/one-api)
- [StockSharp ACTIV Financial connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/activ_financial.html)

ACTIV Financial, AtlasFeed, One API, and their logos are trademarks of their respective owners. The provider's npm package is installed separately under its own license. StockSharp is not affiliated with or endorsed by ACTIV Financial or Options Technology.
