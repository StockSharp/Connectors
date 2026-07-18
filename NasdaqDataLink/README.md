# Nasdaq Data Link connector for StockSharp

This connector reads the official Nasdaq Data Link time-series API at `https://data.nasdaq.com/api/v3/`. It provides catalog lookup and historical observations for financial, economic, commodity, index, and alternative-data series. It calls the documented REST endpoints directly and does not scrape the web site or simulate a streaming transport.

## Supported functionality

- Dataset catalog search with an optional database-code filter and API pagination.
- Exact dataset lookup by the native `DATABASE/DATASET` identifier, for example `FRED/GDP`.
- Current and historical time-series observations through the official dataset-data endpoint.
- Scalar observations, rates, yields, settlement values, and price-series fields as StockSharp Level1 changes.
- Native daily OHLCV candles when a daily dataset actually contains Open, High, Low, and Close columns.
- Optional mapping overrides for scalar value, OHLC, volume, and open-interest columns.
- Ascending range requests, latest-value requests, count-limited history, UTC date normalization, rate-limit handling, and retries for transient server errors.

Dataset metadata, search pages, errors, observations, and heterogeneous row values use dedicated protocol DTOs. The array-shaped observation format is parsed by a typed streaming converter. The connector does not use `JObject`, `JArray`, `JToken`, `dynamic`, anonymous wire objects, or dictionary-shaped protocol models.

## Configuration

- `Token` is the Nasdaq Data Link API key. The connector sends it through the official `X-Api-Token` HTTP header.
- `Address` defaults to `https://data.nasdaq.com/api/v3/`.
- `DatabaseCode` optionally restricts catalog searches to one database, such as `FRED`.
- `SecurityType` is the StockSharp security type assigned to catalog results. It defaults to `Index`, which is suitable for many economic and reference series but can be changed for a specific database.
- `Currency` optionally assigns a currency to returned securities.
- Column settings override automatic field matching. Names are matched case-insensitively while ignoring spaces and punctuation. A configured name that is absent from the response produces an explicit error instead of silently mapping another field.

Use securities returned by lookup whenever possible. The connector board is `NASDAQDL`, while the native identifier remains `DATABASE/DATASET`.

## Data semantics and limitations

Nasdaq Data Link time-series datasets are heterogeneous. A scalar observation is emitted as `ClosePrice`; it is not presented as a last trade. Recognized native Open, High, Low, Close, Volume, and Open Interest columns are mapped to their corresponding Level1 fields. Text and boolean values remain represented in the typed protocol row but are not coerced into market prices.

Candles are advertised only at one day. They are emitted only when the dataset's native frequency is daily and all four native OHLC columns exist and contain values. The API's `collapse` option selects a period's last observation and is therefore not used to manufacture weekly or monthly OHLC candles. Intraday bars, ticks, order books, and trades are not synthesized.

This connector intentionally covers the Data Link time-series API. Nasdaq Data Link Tables have database-specific schemas and are not blindly normalized into market messages. Nasdaq Cloud Data Service is a separate commercial exchange-data API and belongs in its own connector. The Data Link REST API has no market-data WebSocket feed, so subscriptions return the requested snapshot or history and then finish.

API keys, anonymous-access limits, premium database entitlements, row limits, update schedules, redistribution rights, and retained history vary by dataset and account. An HTTP success does not guarantee that a particular premium series is licensed.

The API provides data rather than brokerage services. The connector therefore does not advertise portfolios, positions, orders, or executions.

## Official documentation

- [Nasdaq Data Link documentation](https://docs.data.nasdaq.com/)
- [Time-series API usage](https://docs.data.nasdaq.com/v1.0/docs/in-depth-usage)
- [Time-series parameters](https://docs.data.nasdaq.com/docs/parameters-2)
- [Official Nasdaq Data Link Python client](https://github.com/Nasdaq/data-link-python)
- [StockSharp Nasdaq Data Link connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/nasdaq_data_link.html)

Nasdaq and Nasdaq Data Link are trademarks of Nasdaq, Inc. StockSharp is not affiliated with or endorsed by Nasdaq.
