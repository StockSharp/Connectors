# ORATS connector for StockSharp

This connector integrates StockSharp with the current ORATS Data API. ORATS is a US equity-options data and analytics provider; the connector exposes reference data, option quote analytics, historical observations, and daily underlying prices. It does not expose brokerage accounts or order routing.

## Supported functionality

- ORATS underlying-ticker lookup and actual option-contract lookup from current chains or an exact OCC contract.
- Current delayed or live stock quote snapshots through the documented `strikes/options` endpoint.
- Current and historical option BBO, volume, open interest, theoretical value, implied volatility, delta, gamma, vega, and the unambiguous call-side theta and rho fields.
- Top-of-book market-depth snapshots for stocks and options, plus historical option BBO observations.
- Historical stock Level1 observations and genuine one-day OHLC candles from `hist/dailies`, with adjusted or unadjusted provider fields.
- OCC-compatible option identifiers, bounded JSON responses, API errors, `Retry-After` handling, and retries for HTTP 429 and transient server failures.

## Requirements and configuration

An ORATS Data API token and a plan entitled to the requested endpoints are required. Monthly allowances depend on the subscription. ORATS documents a global limit of 1,000 requests per minute and a maximum of ten symbols on endpoints that accept comma-delimited tickers.

- `Token` is read from the ORATS dashboard and is sent only as the documented `token` query parameter.
- `Address` defaults to `https://api.orats.io/datav2/` and must remain an absolute HTTPS URI.
- `DataMode` selects the approximately 15-minute delayed endpoints or the live endpoints. Live access requires the provider's market-data agreements and returns calculated data with less than ten seconds of delay.
- `PriceAdjustment` selects adjusted or unadjusted daily stock OHLC fields.
- `MarketTimeZoneId` defaults to `America/New_York`, with the Windows-compatible `Eastern Standard Time` fallback.
- `SessionStart` and `SessionEnd` place offset-free end-of-day trade dates into a UTC market session and default to 09:30-16:00 US Eastern.

Use securities returned by lookup whenever possible. An option can also be requested with compact or space-padded OCC symbology. Broad option lookup requires an underlying symbol; the connector never invents contracts from strike grids.

## Data semantics and limitations

The ORATS API is REST-only and does not document a WebSocket feed. A StockSharp market-data subscription therefore performs one REST snapshot or finite history request and finishes. The connector does not hide a polling loop behind a realtime subscription.

ORATS does not publish individual exchange trade events through the implemented JSON endpoints, so trade ticks are not advertised or synthesized. An ORATS end-of-day option strike row is a quote-and-analytics observation, not an OHLC candle. Stock candles come only from the documented daily-price endpoint. Historical stock dailies contain no bid/ask fields, so historical stock market depth is unavailable.

The provider documents `delta` in the side-by-side strike model as call-equivalent delta, including exact put responses. The connector converts it to put delta by subtracting one. Gamma and vega apply to both sides. Because the shared strike row does not provide independently named put theta and rho, those two fields are emitted only for calls rather than mislabeled on puts.

On live endpoints, ORATS calculates `stockPrice` using put-call parity; the provider warns that it may not equal the exact stock price until the data is 15 minutes old. It is exposed as a Level1 value, never as an exchange trade tick.

ORATS also offers one-minute CSV responses, gzip bulk files, and hundreds of proprietary research fields. Those products remain available through ORATS, but they are not forced into unrelated StockSharp message types by this JSON connector.

## Official documentation

- [ORATS API documentation](https://orats.com/docs)
- [Authentication and data formats](https://orats.com/docs/authentication)
- [Delayed Data API](https://orats.com/docs/delayed-data-api)
- [Live Data API](https://orats.com/docs/live-data-api)
- [Historical Data API](https://orats.com/docs/historical-data-api)
- [StockSharp ORATS connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/orats.html)

Data provided by Option Research & Technology Services, LLC. ORATS and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by ORATS.
