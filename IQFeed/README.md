# StockSharp IQFeed connector

The connector integrates StockSharp with DTN IQFeed through the official IQConnect
6.2 TCP services. It communicates directly with the local Level 1, Level 2,
lookup, administration, and derivatives sockets; it does not scrape the IQFeed
user interface or run a separate custom gateway.

## Supported data

- live Level 1 quotes and trades with a configurable dynamic field set;
- live price-level depth and market-by-order data when the symbol and entitlement
  expose the corresponding Level 2 feed;
- historical ticks and intraday, daily, weekly, and monthly candles;
- live interval, tick, and volume candles from the derivatives service;
- symbol lookup and optional bulk symbol import from DTN's official archive;
- streaming headlines, historical headline lookup, and story retrieval;
- protocol negotiation, separate socket parsers, reconnect, subscription cleanup,
  and IQConnect administration authentication.

Coverage depends on the IQFeed subscription and exchange entitlements. IQFeed can
provide US and Canadian equities, options, futures, indices, FX, fixed income,
commodities, market statistics, and other DTN datasets.

## Connection

Install the current Windows IQFeed client and ensure `IQConnect.exe` is available
on `PATH`. Configure `Login` and `Password`. The connector supplies StockSharp's
registered product identifier by default; a developer build can set `ProductId`
programmatically when DTN has issued a different identifier. The connector starts
IQConnect when needed and connects to its standard loopback ports:

- administration: `127.0.0.1:9300`;
- Level 1: `127.0.0.1:5009`;
- Level 2: `127.0.0.1:9200`;
- lookup and history: `127.0.0.1:9100`;
- derivative candles: `127.0.0.1:9400`.

Every address can be overridden for an IQConnect installation using different
local ports. `Version` is fixed to the current protocol version 6.2 so every
socket negotiates the same documented wire format.

## Limitations

- This is a market-data connector. IQFeed does not provide order routing,
  portfolios, positions, or brokerage account operations.
- IQConnect and the DTN client login are Windows dependencies. A DTN IQFeed
  subscription, API developer license, and applicable exchange fees are separate.
- Level 2 formats differ by venue. CME market-by-order records are exposed as
  order-log messages; Nasdaq-style records can be aggregated into price-level
  increments by the connector.
- Historical availability, depth, news, and the maximum number of watched symbols
  depend on the selected service plan and entitlements.
- Bulk symbol download is optional because the official archive is large. Normal
  filtered symbol lookup uses the local lookup socket.

## Official documentation

- [IQFeed API and developer access](https://iqhelp.dtn.com/api/)
- [IQFeed protocol overview](https://www.iqfeed.net/dev/api/docs/IQFeedProtocols.html)
- [Initializing IQFeed](https://www.iqfeed.net/dev/api/docs/InitializingTheFeed.html)
- [Level 1 TCP service](https://www.iqfeed.net/dev/api/docs/Level1viaTCPIP.html)
- [Level 2 TCP service](https://www.iqfeed.net/dev/api/docs/Level2viaTCPIP.html)
- [Historical TCP service](https://www.iqfeed.net/dev/api/docs/HistoricalviaTCPIP.html)
- [Symbol lookup TCP service](https://www.iqfeed.net/dev/api/docs/SymbolLookupviaTCPIP.html)
- [Derivative interval data](https://www.iqfeed.net/dev/api/docs/Derivatives_StreamingIntervalBars_TCPIP.html)
