# Goldman Sachs Marquee connector for StockSharp

This connector provides native .NET access to the published Goldman Sachs Developer APIs used by Marquee and GS Quant. It talks directly to the official REST services and does not embed Python or invoke the `gs-quant` package.

## Supported functionality

- OAuth 2.0 client-credentials authentication with automatic token renewal.
- Production and official QA environments.
- Entitlement-aware cross-asset security lookup through the Asset Service, including scrolling through large result sets.
- Ticker, Marquee identifier, asset class, product type, currency, exchange, MIC, and common cross-reference identifiers.
- Level 1 snapshots from the highest-ranked entitled provider for bid, ask, midpoint, trade, open, high, low, close, and volume fields.
- Native end-of-day OHLCV history at the one-day time frame.
- Automatic discovery of the dataset supplying each requested field through the measures-availability endpoint.
- Merging of fields when Goldman Sachs serves OHLCV measures from different entitled datasets.

Every OAuth request, asset query, availability response, dataset query, and market-data row has a dedicated DTO. The connector does not use `JObject`, `JArray`, `JToken`, `dynamic`, anonymous wire objects, or dictionary-shaped protocol models.

## Configuration

- `ClientId` - application identifier created in the Goldman Sachs Developer application portal.
- `ClientSecret` - corresponding OAuth application secret.
- `IsDemo` - uses the official QA environment when enabled and production when disabled.

The connector requests the default read-only GS Quant scopes: `read_content`, `read_product_data`, `read_financial_data`, and `read_user_profile`. The application still receives only the datasets and identifiers enabled by its Goldman Sachs entitlements.

## Data model and limitations

Marquee datasets are entitlement-driven. The connector first requests `/data/measures/{assetId}/availability`, chooses the highest-ranked provider for each field, and then queries the selected dataset. A single candle can therefore combine typed rows from several Goldman Sachs datasets while retaining the native daily values.

The published generic data contract does not define a streaming WebSocket or a universal brokerage order lifecycle. Consequently, Level 1 is exposed as a completed REST snapshot, candles are historical end-of-day data, and the connector does not advertise fabricated streaming or transaction support. Goldman Sachs may expose additional execution services to an institution under separate onboarding documentation; those client-specific contracts are outside this connector.

The QA environment is intended for testing institutional integrations. It is not a public retail paper-trading account. Market-data availability, latency, history, symbology, and licenses depend on the application's entitlements.

## Official documentation

- [Goldman Sachs Developer](https://developer.gs.com/)
- [GS Quant authentication and OAuth sessions](https://developer.gs.com/docs/gsquant/authentication/gs-session/)
- [Assets and Security Master](https://developer.gs.com/docs/gsquant/markets/assets-and-security-master/)
- [Querying datasets](https://developer.gs.com/docs/gsquant/data/accessing-data/querying-data/)
- [Dataset model and limits](https://developer.gs.com/docs/gsquant/data/data-environment/datasets/)
- [Official open-source GS Quant SDK](https://github.com/goldmansachs/gs-quant)
- [StockSharp Goldman Sachs Marquee connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/marquee.html)

Goldman Sachs, Marquee, GS Quant, and their marks are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Goldman Sachs.
