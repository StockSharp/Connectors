# Exegy connector for StockSharp

This connector reads entitled normalized CSV deliveries produced by Exegy Historical Data Service and Exegy Capture Replay (XCR). Exegy officially offers raw PCAP, normalized CSV, and replay through its Exegy Client API (XCAPI). The connector implements the redistributable file boundary and does not invent a public endpoint for customer-only infrastructure.

## Supported functionality

- Security lookup from normalized reference files and identifiers present in market-data rows.
- Tick trades and cancellations.
- Level1 best bid/ask, last trade, status, daily OHLC, cumulative volume, and open interest.
- Level2 Market By Price reconstruction from side, level, price, size, order count, and action fields.
- Market By Order reconstruction and StockSharp order-log messages when order IDs are delivered.
- Exchange, source/appliance, and capture/receive timestamps with nanosecond input accepted and StockSharp precision preserved.
- Plain `.csv`, gzip-compressed `.csv.gz`, and CSV/CSV.GZ entries inside `.zip` deliveries.

## Configuration and schema mapping

Set `DataDirectory` to the root of an entitled normalized delivery. Nested directories are scanned by default without following reparse points. CSV delimiters are detected from comma, semicolon, tab, or pipe headers; quoted fields and escaped quotes are supported.

Exegy data exports are tailored to the customer and selected feed. The header mapper accepts common normalized names for instrument ID, symbol, venue, reference attributes, exchange/source/capture timestamps, message type, action, side, price, size, level, order ID, trade ID, sequence, participant, top of book, trade, status, and daily statistics. Headers can use `Exegy`, `XCAPI`, `MarketData`, or `Field` prefixes. At least an instrument ID or symbol and a complete event timestamp are required.

`DefaultTimeZoneId` defaults to `UTC` and is applied only to timestamps without an explicit offset. ISO timestamps and Unix seconds, milliseconds, microseconds, and nanoseconds are accepted. Timestamp precedence is exchange, source/appliance, capture/receive, then generic timestamp. A time-of-day without a date is rejected instead of being assigned the current date.

For MBP, zero-size/delete events remove the addressed price or level and reset events clear the book. For MBO, the connector retains order state for the current subscription and aggregates orders by side and price for depth snapshots. Order-log rows preserve string order/trade IDs, participant, sequence, price, remaining size, and active/done state.

## Live API boundary

Exegy Axiom can publish through XCAPI or OpenMAMA, and Exegy advertises C++, C#, and Java support. nxFeed and other appliances also expose customer deployment APIs. The Exegy bridge, dictionaries, entitlement configuration, native runtime, credentials, and XCAPI SDK are delivered under commercial agreement and are not published as a redistributable NuGet package or public wire specification. This project therefore does not use reflection around unknown binaries or fabricate UDP/TCP messages. A live adapter can be added when the licensed SDK and a distributable build contract are available.

This connector is finite historical market data only. It does not decode raw PCAP because that requires the native exchange feed specification selected for the capture. It has no transactions, portfolios, accounts, or live subscriptions.

## Official documentation

- [Exegy](https://www.exegy.com/)
- [Historical Market Data](https://www.exegy.com/solutions/historical-market-data/)
- [Exegy Capture Replay](https://www.exegy.com/products/exegy-capture-replay/)
- [Axiom normalized market data feed](https://www.exegy.com/products/marketdataasaservice/)
- [Software Market Data System](https://www.exegy.com/products/smds/)
- [nxFeed](https://www.exegy.com/products/nxfeed/)
- [StockSharp Exegy connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/exegy.html)

Exegy, XCAPI, Axiom, nxFeed, and their logos are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Exegy.
