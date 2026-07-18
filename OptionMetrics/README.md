# OptionMetrics IvyDB connector for StockSharp

This connector reads licensed OptionMetrics IvyDB US end-of-day delivery files. IvyDB is an institutional historical-data product, not a public brokerage API. The connector therefore works with the provider's relational tab-delimited text delivery instead of inventing REST or WebSocket endpoints.

## Supported functionality

- Underlying-security lookup from the latest `IVYSECUR` full copy, enriched with current and historical aliases from `IVYSECNM`.
- Actual option-contract lookup from an `IVYOPPRC` daily table, preserving the permanent Security ID, Option ID, provider symbol, strike, expiration, call/put side, and contract size.
- Historical underlying Level1 observations from `IVYSECPR`, respecting the documented positive high/low and negative closing-bid/ask conventions.
- Historical option Level1 observations with closing bid, offer, volume, open interest, implied volatility, delta, gamma, vega, and theta.
- Historical top-of-book snapshots for options and for underlying rows that contain genuine closing bid/ask values.
- Genuine one-day underlying OHLC candles. A negative close is a provider midpoint and is never mislabeled as a trade or candle close.
- Raw, split-adjusted, or total-return-adjusted underlying prices using the cumulative factors supplied by IvyDB.

The reader uses concrete typed records for every supported table. It does not use `JObject`, `JArray`, `JToken`, `dynamic`, protocol dictionaries, or `object[]`.

## Files and configuration

Set `DataDirectory` to a directory containing expanded `.txt` files, `.zip` archives, or both. Subdirectories are scanned without following reparse points. ZIP entries are streamed directly and are never extracted. If the same table date exists both as a text file and inside an archive, the expanded text file takes precedence.

The connector recognizes standard daily names with an optional `D` delivery marker:

- `IVYSECUR.yyyymmddD.txt` — Security.
- `IVYSECNM.yyyymmddD.txt` — Security Name history.
- `IVYSECPR.yyyymmddD.txt` — Security Price.
- `IVYOPPRC.yyyymmddD.txt` — Option Price and analytics.

The stable leading columns documented by OptionMetrics are parsed and additional columns appended by newer IvyDB releases are left available for future typed mappings. Malformed data is rejected with the table, source file, and line number. The reader does not silently discard corrupt rows.

`MarketTimeZoneId` defaults to `America/New_York` and falls back to the Windows identifier `Eastern Standard Time`. `SessionStart` and `SessionEnd` place daily underlying data into a US market session. `OptionSnapshotTime` defaults to 15:59 Eastern, the documented quote time used from July 30, 2009 onward; change it when processing an older delivery whose reference manual specifies a different time.

## Data semantics and limitations

IvyDB US is end-of-day historical data. There is no live subscription, order routing, account access, or public WebSocket feed in this connector. Every StockSharp subscription is finite and finishes after the matching local rows are read.

Option Price supplies a closing BBO and analytics, not trades or OHLC bars, so the connector does not synthesize option ticks or candles. Quote sizes are not present in the supported table and market-depth volumes are therefore zero. Option open interest in standard IvyDB US deliveries can reflect the provider's documented reporting lag. Sentinel analytics such as `-99.99` are treated as unavailable rather than valid Greeks.

Underlying Security Price fields are interpreted exactly as documented: positive bid/low and ask/high values are daily low and high; negative values are closing bid and ask on a no-trade day. A positive close is a closing trade, while a negative close is the absolute bid/ask midpoint. Split adjustment uses `row cumulative factor / latest cumulative factor`; total-return adjustment uses the corresponding total-return factor. Volume is kept in provider units.

The standard daily correction-patch workflow and tables such as volatility surfaces, zero curves, dividends, distributions, forward prices, historical volatility, signed volume, and IvyDB US Intraday are separate licensed schemas. Apply correction patches with the official OptionMetrics loading workflow before reading the resulting daily tables. Unsupported tables are not guessed into unrelated StockSharp message types.

Use securities returned by lookup whenever possible. The serialized native ID retains the permanent Security ID and, for options, the Option ID; this avoids ambiguity after ticker, strike, or deliverable changes. A plain option symbol is accepted for convenience, but adjusted contracts sharing a symbol should be selected through lookup.

## Official documentation

- [OptionMetrics IvyDB US product](https://optionmetrics.com/united-states/)
- [OptionMetrics data products](https://optionmetrics.com/data-products/)
- [OptionMetrics support](https://optionmetrics.com/support-request/)
- [StockSharp OptionMetrics connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/optionmetrics.html)

IvyDB data is licensed separately from OptionMetrics. OptionMetrics, IvyDB, and their logos are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by OptionMetrics.
