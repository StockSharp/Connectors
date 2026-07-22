# J.P. Morgan DataQuery connector for StockSharp

This connector provides native .NET access to the published J.P. Morgan DATAQUERY JSON Data API. It follows the current official DataQuery SDK wire contract directly and does not embed Python or invoke a Python subprocess.

## Supported functionality

- OAuth 2.0 client-credentials authentication with the production DataQuery audience and automatic token renewal.
- Entitlement-aware instrument listing and keyword search within a configured DataQuery group.
- Instrument identifiers, names, currency, country, ISIN, and CUSIP metadata.
- Historical time-series retrieval by instrument and DataQuery attribute.
- Cursor pagination with loop and page-count protection.
- Retry handling for rate limits and transient server failures.
- Historical and latest-value output as StockSharp Level1 messages.

## Configuration

- `Key` - client identifier issued during J.P. Morgan DataQuery onboarding.
- `Secret` - corresponding OAuth client secret.
- `GroupId` - an entitled DataQuery group, for example `FI_GO_BO_EA`.
- `Attribute` - a time-series attribute available in that group, for example `MIDPRC`.
- `ValueField` - the StockSharp Level1 field that receives the selected attribute. The default maps `MIDPRC` to `SpreadMiddle`; select another field only when its meaning matches the chosen DataQuery attribute.
- `SecurityType` - optional StockSharp security type for the configured group. It is deliberately not guessed from an opaque group identifier.

The connector uses the official production API at `https://api-dataquery.jpmchase.com/research/dataquery-authe/api/v2/`, the OAuth endpoint at `https://authe.jpmorgan.com/as/token.oauth2`, and the published production audience `JPMC:URI:RS-06785-DataQueryExternalApi-PROD`.

## Data model and limitations

DataQuery is an institutional, entitlement-controlled research and markets data service. A group defines its own instrument population and available attributes. The connector therefore requires a group identifier instead of pretending that all DataQuery datasets share one global security master. Security lookup uses the selected group; time-series requests use the native instrument and attribute identifiers.

The JSON Data API returns dated numeric observations rather than exchange trades or OHLC candles. The connector exposes those observations as completed Level1 history and lets the user choose the semantically correct StockSharp field. It does not turn midpoint observations into trades or synthesize candles.

The public DataQuery contract has no live price WebSocket and no brokerage order lifecycle. The SDK's SSE channel reports file-delivery availability, not market ticks. Consequently this connector does not advertise streaming, order entry, portfolios, or executions. J.P. Morgan Fusion file delivery and institution-specific execution products use separate contracts and are outside this connector.

Access, datasets, history, redistribution rights, and latency depend on the institution's J.P. Morgan agreement and entitlements. This is not a public retail or paper-trading API.

## Official documentation

- [J.P. Morgan DataQuery SDK](https://github.com/jpmorganchase/dataquery-sdk)
- [DataQuery SDK documentation](https://jpmorganchase.github.io/dataquery-sdk/)
- [J.P. Morgan Fusion](https://fusion.jpmorgan.com/)
- [J.P. Morgan Developer](https://developer.jpmorgan.com/)
- [StockSharp J.P. Morgan DataQuery connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/jpm_dataquery.html)

J.P. Morgan, DATAQUERY, Fusion, and their marks are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by JPMorgan Chase & Co.
