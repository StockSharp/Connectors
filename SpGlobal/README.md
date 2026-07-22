# S&P Global Commodity Insights connector for StockSharp

This connector provides native .NET access to the current S&P Global Energy (formerly Commodity Insights/Platts) Market Data API. It follows the official HTTPS contract and official `spgi-python` SDK behavior directly; it does not launch Python or scrape the developer portal.

## Supported functionality

- Username/password token generation at `/auth/api`, bearer-token caching, and automatic renewal after expiry or an HTTP 401/403 response.
- Current production endpoint `https://api.ci.spglobal.com/`, with a configurable base address for accounts still provisioned on `https://api.platts.com/`.
- Entitlement-aware Platts symbol search and pagination through `/market-data/reference-data/v3/search`.
- Optional Market Data Category, commodity, contract type, and assessment-frequency filters.
- Symbol description, commodity, contract type, currency, unit, delivery basis, curve, quotation style, assessment frequency, and MDC reference fields.
- Current and historical price assessments by Platts symbol through the native v3 value endpoints.
- Configurable bate filtering (`c` by default), assessment-date ranges, corrections metadata, previous-value metadata, and percentage changes.
- Rate-limit and transient-server retry handling.

## Configuration

- `Login` and `Password` are the S&P Global Energy API credentials issued to the user or organization.
- `Address` defaults to the current production host. Change it only when S&P Global has explicitly provisioned the account on another host.
- `MarketDataCategory`, `Commodity`, `ContractType`, and `AssessmentFrequency` optionally narrow security lookup. Empty values do not add filters.
- `Bate` selects the assessment timing/value code. It defaults to `c`, the closing assessment used in S&P Global's official market-data examples. Clear it only when all entitled bates are intentionally required.

Use a security returned by lookup whenever possible. The seven-character Platts symbol is stored in both `SecurityCode` and `SecurityId.Native`, and subsequent assessment requests use that stable identifier.

## Data model and limitations

An assessment is a published commodity valuation, not an exchange trade. The native `value` field is therefore exposed as `ClosePrice`, never as a fabricated last trade. The API's `deltaPercent` is exposed as the StockSharp Level1 `Change` field.

Historical assessments are scalar values grouped by symbol and bate. They are not OHLCV bars, so this connector does not advertise candles. The published Market Data API is request/response HTTPS and does not provide a WebSocket order book or brokerage operations; consequently the connector does not advertise ticks, market depth, live streaming, orders, portfolios, positions, or executions.

Access is paid and entitlement-based. Available symbols, bates, history depth, quotas, redistribution rights, and whether the legacy or current hostname applies are controlled by the customer's S&P Global agreement.

## Official documentation

- [S&P Global Energy API getting started](https://developer.spglobal.com/energy/delivery-solutions/api/getting-started)
- [S&P Global Energy developer portal](https://developer.spglobal.com/energy/)
- [Official S&P Global Commodity Insights Python SDK](https://github.com/spgi-ci/spgci-python)
- [StockSharp S&P Global Commodity Insights connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/sp_global_commodity_insights.html)

S&P Global, Commodity Insights, and Platts are trademarks of S&P Global. StockSharp is not affiliated with or endorsed by S&P Global.
