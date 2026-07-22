# AlgoSeek connector for StockSharp

This connector reads licensed AlgoSeek historical delivery files for US equities, OPRA equity options, US futures, and options on futures. AlgoSeek bulk data is an institutional product delivered as CSV/CSV.GZ through provider-managed cloud or file channels; it is not a single public brokerage API. The connector therefore reads an entitled local or mounted delivery directory and does not invent shared REST, WebSocket, authentication, or order-routing endpoints.

## Supported functionality

- Security lookup from the actual symbols and contract attributes present in licensed files.
- Equity TAQ and Trade Only tick trades, including provider cancellation flags and hexadecimal conditions.
- Consolidated equity NBBO updates by default, with an optional venue-BBO mode.
- Options TANQ trades, cancellations, NBBO quotes, and open interest, preserving ticker, expiration, strike, and call/put side in the native security ID.
- Futures and futures-options TAQ trades, bid/ask updates, aggressor side, open interest, empty-book events, provider Security ID, and documented calculated-price filtering.
- Equity Trade Only one-minute candles and Standard Daily OHLC candles.
- Options TAQ one-minute trade candles and closing NBBO observations.

## Files and configuration

Set `DataDirectory` to the root of one licensed AlgoSeek delivery. The connector recognizes:

- expanded `.csv` files;
- gzip-compressed `.csv.gz` files;
- CSV or CSV.GZ entries inside `.zip`, `.tar`, `.tar.gz`, and `.tgz` containers.

Entries are streamed directly and are never extracted. Nested directories are scanned by default without following reparse points. Archive entry names are cataloged, but their market rows are not decompressed during connection.

AlgoSeek normally organizes tick and minute data as one file per symbol or option contract per trading date. Security lookup therefore reads only the first row of ordinary equity/futures/option-contract TAQ files. It reads all distinct contracts only for options chain minute bars and open-interest files, and all symbols for multi-security daily files. This preserves the official organization without attempting to load a multi-terabyte delivery into memory.

`MarketTimeZoneId` defaults to `America/New_York` and falls back to `Eastern Standard Time` on Windows. It is used for the documented ET timestamps in equities and OPRA options. Futures events use `UTCDate` and `UTCTime` directly; their Chicago-local fields are retained only as source context and are not used to reconstruct UTC.

## Data semantics and limitations

The connector is finite historical market data only. It has no live subscription, transaction, portfolio, or account functionality. Point separate adapter instances at separate AlgoSeek products when the same market/date range exists in multiple licensed datasets; otherwise overlapping TAQ and Trade Only files will correctly expose both provider event streams.

Equity TAQ contains venue BBO and separate NBBO rows. `IsNationalBestQuotesOnly` defaults to `true`, so Level1 and depth use only the consolidated rows. When disabled, NBBO duplicates are ignored and the adapter calculates the best price from per-venue quote state. Trade rows are preserved as published, including both exchange and `TRADE NB` events where present; consumers can use the provider conditions when selecting a research universe.

Options use a generated display code because TANQ identifies a contract with the tuple ticker, expiration, call/put, and strike rather than an OCC-formatted contract symbol. Always subscribe with a security returned by lookup. Futures expiry dates are not synthesized from a one-digit ticker year code; the provider ticker and Security ID remain authoritative.

AlgoSeek futures flag `8` denotes a calculated price rather than a real execution, so those rows are excluded from StockSharp ticks. Equity and option trade cancellations are emitted as cancellation executions instead of being silently converted into new trades. Nanosecond equity/futures timestamps are truncated only beyond the 100-nanosecond precision supported by `DateTime`.

The current schemas cover Equity TAQ/Trade Only, Options TANQ/Open Interest, Futures/Future Options TAQ, Equity Trade Only Minute Bars, Options TAQ Minute Bars, and Standard Equity Daily OHLC. Other AlgoSeek products such as full depth, analytics, adjustment factors, security masters, continuous futures, and custom SQL exports have distinct documented schemas and are not guessed into these message types.

## Official documentation

- [AlgoSeek](https://algoseek.com/)
- [US Equity Trade and Quote guide](https://us-equity-market-data-docs.s3.us-east-1.amazonaws.com/algoseek.US.Equity.TAQ.pdf)
- [US Equity Trade Only Minute Bar guide](https://us-equity-market-data-docs.s3.us-east-1.amazonaws.com/algoseek.US.EquityTrades.Only.Minute.Bars.pdf)
- [US Options Trade and NBBO Quote guide](https://us-options-market-data-docs.s3.us-east-1.amazonaws.com/algoseek.US.Options.TANQ.pdf)
- [US Options Trade and Quote Minute Bar guide](https://us-options-market-data-docs.s3.us-east-1.amazonaws.com/algoseek.US.Options.TAQ.Minute.Bars.pdf)
- [US Futures Trade and Quote guide](https://us-futures-market-data-docs.s3.us-east-1.amazonaws.com/algoseek.US.Futures.TAQ.pdf)
- [StockSharp AlgoSeek connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/algoseek.html)

AlgoSeek data is licensed separately from the provider. AlgoSeek and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by AlgoSeek.
