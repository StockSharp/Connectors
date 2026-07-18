# QuantHouse QuantFEED connector for StockSharp

This connector reads entitled QuantHouse Historical On-Demand (HOD) CSV deliveries. QuantHouse documents HOD as normalized tick-by-tick file delivery with Level1, Level2 Market By Level, reference data, and three event timestamps. HOD explicitly has no API integration, so the connector consumes the delivered files directly instead of inventing a REST or socket endpoint.

## Supported functionality

- Security lookup from QuantHouse reference files and market-data rows.
- Tick trades from event-style or wide Last Trade fields.
- Level1 bid, ask, last trade, trading status, daily OHLC, volume, and open interest.
- Level2 Market By Level snapshots reconstructed from typed side/level/action events.
- Market, server, and capture timestamps, with market time used first and all emitted times normalized to UTC.
- Plain `.csv`, gzip-compressed `.csv.gz`, and CSV/CSV.GZ entries inside `.zip` deliveries.

Every recognized source record is converted to a concrete reference or market-data DTO. The implementation does not use `JObject`, `JArray`, `JToken`, `dynamic`, protocol dictionaries, or `object[]`.

## Configuration

Set `DataDirectory` to the root of one entitled HOD delivery. Nested directories are scanned by default without following reparse points. CSV delimiters are detected from comma, semicolon, tab, or pipe headers; quoted fields and escaped quotes are supported.

`DefaultTimeZoneId` defaults to `UTC` and is applied only when a timestamp has no explicit offset. ISO timestamps and Unix seconds, milliseconds, microseconds, and nanoseconds are accepted. For each event, timestamp precedence is market, server, capture, then generic event timestamp. File-name dates are used only to avoid opening files outside a requested range, with a one-day boundary allowance for global time zones; row timestamps remain authoritative.

QuantHouse exports can select different FeedOS fields. The typed header mapper recognizes common FeedOS/HOD names for:

- `FeedOSInstrumentCode`, local symbol, MIC, instrument name/type, currency, ISIN, expiry, strike, call/put, tick size, and multiplier;
- market/server/capture timestamps, event type, side, level, update action, price, quantity, order count, and sequence;
- wide best bid/ask, last trade, trading status, daily OHLC, volume, and open interest.

At least a FeedOS instrument code or local symbol and an event timestamp must be present in market-data files. Use a security returned by lookup when the same local symbol exists on multiple MICs.

## Product boundary

Live QuantFEED and proprietary historical binary files use the customer-only FeedOS SDK. QuantHouse currently advertises C++, Java, and .NET C# client APIs, but does not publish the SDK, NuGet package, wire specification, credentials, or redistributable binaries publicly. This repository therefore does not contain a speculative TCP implementation or redistribute vendor components. A live adapter can be added when a licensed, redistributable SDK is made available to the project.

This connector is finite historical market data only. It has no transaction, portfolio, account, or live subscription functionality. Its Level2 state follows the update actions present in the delivery; malformed rows fail with the source and line number instead of being silently coerced.

## Official documentation

- [QuantHouse](https://www.quanthouse.com/)
- [QuantFEED](https://www.quanthouse.com/quantfeed/)
- [Historical On-Demand](https://www.quanthouse.com/hod/)
- [Historical Binary Files](https://www.quanthouse.com/historical-binary-files/)
- [ConsolidatedFEED](https://www.quanthouse.com/consolidatedfeed/)
- [StockSharp QuantFEED connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/quantfeed.html)

QuantHouse, QuantFEED, FeedOS, and their logos are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by QuantHouse.
