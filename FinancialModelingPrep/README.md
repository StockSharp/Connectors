# Financial Modeling Prep connector for StockSharp

This connector integrates StockSharp with Financial Modeling Prep (FMP) through the provider's stable REST API and market-specific WebSocket products. It covers global stocks and funds, forex, cryptocurrencies, indices, commodities, historical candles, realtime Level1 data, genuine trade ticks where the feed supplies trades, and financial news. FMP is a data provider in these APIs, so the connector does not expose portfolios or order routing.

## Supported functionality

- API-key authentication through the documented `apikey` HTTP header, keeping the credential out of request URLs.
- Security lookup through symbol and name search, the stock screener, and dedicated forex, cryptocurrency, index, and commodity lists.
- Native identifiers that preserve the FMP market family, exact symbol, and exchange code.
- REST Level1 snapshots for every supported market family.
- Genuine WebSocket top-of-book and trade updates for entitled US equities, forex, and cryptocurrencies, using the provider's separate endpoints.
- Live tick trades for US equities and cryptocurrencies. Quote updates and trade-break messages are not mislabeled as trades.
- Historical candles at 1, 5, 15, and 30 minutes, 1 and 4 hours, and one day where the corresponding stable endpoint is documented for the market family.
- Adjusted, non-split-adjusted, and dividend-adjusted daily stock histories; the documented `nonadjusted` option is also applied to intraday stock data when selected.
- Latest and symbol-filtered stock, forex, and crypto news with date filters, pagination, count limits, UTC normalization, and duplicate-link removal.
- Lazy WebSocket connections, bounded frames, reconnect with subscription restoration, and explicit propagation of authentication, entitlement, rate-limit, and protocol failures.

## Configuration

- `Token` is the API key from the FMP dashboard.
- `Address` defaults to the official `https://financialmodelingprep.com/stable/` REST base address.
- `StockWebSocketAddress`, `ForexWebSocketAddress`, and `CryptoWebSocketAddress` default to the three official market-specific streaming endpoints.
- `StockExchange` is optional. When set, it filters stock searches and full stock lookup and qualifies manually entered stock identifiers. Leave it empty for global lookup.
- `EodAdjustment` selects adjusted, non-split-adjusted, or dividend-adjusted daily stock history. Dividend adjustment is an end-of-day product and is not applied to intraday bars.
- `IntradayTimeZoneId` defaults to `UTC`. FMP intraday responses contain offset-free date strings; set this to the installed system time-zone identifier that matches the entitled dataset if the provider supplies it in another zone. The connector converts the result to UTC at the protocol boundary.

Use securities returned by lookup whenever possible. StockSharp's `Native` identifier retains the provider's market and exchange identity while `SecurityCode` remains the familiar ticker, currency pair, crypto pair, index, or commodity code.

## Data semantics and limitations

Endpoint availability, delay, history depth, exchange coverage, and request allowance depend on the FMP subscription. A successful API key does not imply entitlement to every REST dataset or WebSocket product. HTTP 401/403, rate-limit responses, and server failures are returned to StockSharp instead of being treated as empty data.

FMP currently presents the detailed company, forex, and crypto WebSocket protocol pages as legacy documentation, while still advertising WebSocket datasets and publishing the three endpoints. The connector implements exactly the documented `login`, `subscribe`, and `unsubscribe` messages and `T` trade, `Q` quote, and `B` trade-break event semantics. It does not replace unavailable streaming access with REST polling. The stock stream is documented for US equities; known non-US exchange identifiers therefore finish after the REST Level1 snapshot.

The REST API does not expose a normalized historical tick-trade endpoint, so historical tick subscriptions are rejected. Forex realtime events are exposed as Level1 data only; the connector does not claim forex tick trades even though the shared WebSocket schema can contain trade-shaped fields.

FMP documents the full 1, 5, 15, and 30 minute plus 1 and 4 hour set for stocks. The index, commodity, forex, and crypto sections document 1 minute, 5 minute, and 1 hour intervals; the connector also supports their common daily endpoint and rejects undocumented intraday combinations.

FMP also publishes fundamentals, financial statements, estimates, ratings, and specialized analytics. StockSharp's normalized message model used by this connector has no direct fundamentals transport, so those datasets are not forced into unrelated Level1 or news messages.

## Official documentation

- [FMP stable API documentation](https://site.financialmodelingprep.com/developer/docs/stable)
- [FMP API quickstart](https://site.financialmodelingprep.com/developer/docs/quickstart)
- [Stable chart endpoints](https://site.financialmodelingprep.com/developer/docs/stable/historical-price-eod-full)
- [Stable stock news search](https://site.financialmodelingprep.com/developer/docs/stable/search-stock-news)
- [Company WebSocket protocol](https://site.financialmodelingprep.com/developer/docs/websocket-api)
- [Crypto WebSocket protocol](https://site.financialmodelingprep.com/developer/docs/crypto-websocket)
- [Forex WebSocket protocol](https://site.financialmodelingprep.com/developer/docs/forex-websocket)
- [StockSharp Financial Modeling Prep connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/fmp.html)

Data provided by Financial Modeling Prep. FMP and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by FMP.
