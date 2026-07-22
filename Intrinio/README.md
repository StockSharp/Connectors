# Intrinio connector for StockSharp

This connector provides native .NET access to Intrinio's official REST API v2 and official real-time equities and options WebSocket SDK. It supports US security and option reference data, current snapshots, historical trades, native candles, company news, and continuous trade and top-of-book subscriptions. It does not scrape Intrinio pages and does not disguise REST polling as streaming.

## Supported functionality

- API-key authentication against the configurable `https://api-v2.intrinio.com/` REST base address.
- Active US security lookup and text search, including Intrinio IDs, exchange MICs, currency, FIGI identifiers, and security classifications.
- Option-contract lookup by underlying, strike, expiration, type, or exact compact/padded OSI code.
- Current equity Level1 snapshots from the real-time price endpoint, supplemented by entitled composite quote fields.
- Current option Level1 snapshots with NBBO, last trade, volume, open interest, daily OHLC, mark, implied volatility, Greeks, and underlying price where entitled.
- Genuine continuous equities trades and bid/ask updates through Intrinio's binary WebSocket protocol and the official `IntrinioRealTimeClient` SDK.
- Genuine continuous options trades, conflated NBBO updates, and untimed refresh records through the official SDK. Refresh OHLC/open-interest values are cached and attached to the next timestamped trade or quote; the connector does not invent a receipt timestamp.
- Historical equity and option trades through the documented REST endpoints.
- Native equity intervals for 1, 5, 10, 15, 30, and 60 minutes, plus daily, weekly, monthly, quarterly, and yearly end-of-day prices.
- Native option intervals for 1, 5, 10, 15, 30, and 60 minutes, plus daily option end-of-day OHLC.
- Historical company news globally or filtered by a security ticker.
- Pagination guards, rate-limit and transient-server retries, UTC normalization, and separate `INTRINIO` and `INTRINIOOPT` StockSharp boards.

## Configuration

- `Token` is the Intrinio production API key shown in the Intrinio account portal. REST API v2 sends it through the officially documented `api_key` query parameter. Error messages deliberately remove the query string so the key is not logged.
- `Address` defaults to the official REST API v2 host.
- `EquityProvider` selects the entitled official stream: legacy real-time/IEX, delayed SIP, Nasdaq Basic, Cboe One, IEX, or Equities Edge. The same selection is translated to each REST endpoint's documented source vocabulary.
- `OptionProvider` selects OPRA or Options Edge for WebSocket delivery.
- `IsDelayedOptions` selects the 15-minute delayed options service for both REST and WebSocket requests.
- `IsAdjusted` uses Intrinio's adjusted equity end-of-day OHLC and volume fields. Intraday split adjustment is requested directly from the interval endpoint.
- Thread and buffer settings are passed to the official binary decoder. Intrinio requires buffers of at least 2048; trade-and-quote workloads generally need more workers than trade-only workloads.

Use securities returned by lookup whenever possible. Equity native IDs retain Intrinio's internal ID. Option security codes use compact OSI form (`AAPL261218C00230000`) while streaming joins use the official six-character underscore-padded form (`AAPL__261218C00230000`).

## Data semantics and limitations

Intrinio products are commercial and independently entitled. A valid API key does not imply access to every equities source, OPRA, Options Edge, end-of-day data, news, fundamentals, or derived Greeks. The connector exposes only fields actually returned by the entitled endpoint.

REST trade history is limited by Intrinio to the latest seven days and a maximum seven-day request range. The adapter rejects older requests instead of returning a misleading partial range. Equity interval sources use a different documented vocabulary from individual trade sources; unsupported combinations such as Equities Edge historical trades are rejected explicitly while their live WebSocket feed remains available.

A Level1 subscription with `Count = 1` performs a current REST snapshot and finishes. A Level1 subscription without that one-record limit opens the genuine WebSocket feed. Historical quote events are not available through the REST contract and are not synthesized.

Candles are returned from Intrinio's native interval and end-of-day endpoints and then finish. They are not built from arbitrary REST snapshots. The option interval endpoint accepts an end time but no start-time parameter, so the connector filters its returned page to the requested range. Option end-of-day data supports daily candles only; equity end-of-day data additionally supports Intrinio's weekly, monthly, quarterly, and yearly frequencies.

News is a paged REST dataset, not part of the market-data WebSocket protocol. News requests therefore return the entitled latest/history page and finish. Intrinio also offers extensive fundamentals and alternative datasets, but StockSharp has no generic wire message for arbitrary Intrinio data tags; the connector does not force those values into unrelated market fields. Entitled P/E and other standard quote fields are mapped where StockSharp has an exact Level1 equivalent.

The official SDK manages WebSocket authentication, proprietary binary decoding, reconnect, self-healing, and channel restoration. Its synchronous callbacks are moved to a single-reader asynchronous queue before StockSharp messages are emitted, as recommended by Intrinio for high-volume quote feeds.

## Official documentation

- [Intrinio API documentation](https://docs.intrinio.com/)
- [REST API v2 getting started](https://docs.intrinio.com/documentation/api_v2/getting_started)
- [Web API tutorial and API-key authentication](https://docs.intrinio.com/tutorial/web_api)
- [Real-time WebSocket tutorial](https://docs.intrinio.com/tutorial/websocket)
- [Official .NET real-time SDK](https://github.com/intrinio/intrinio-realtime-csharp-sdk)
- [Official .NET REST SDK and generated API reference](https://github.com/intrinio/csharp-sdk)
- [Security code reference](https://docs.intrinio.com/documentation/security_codes)
- [StockSharp Intrinio connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/intrinio.html)

Intrinio is a trademark of Intrinio, Inc. StockSharp is not affiliated with or endorsed by Intrinio.
