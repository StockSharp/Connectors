# KuCoin Historical Data Connector

The **KuCoin Historical Data connector** imports public KuCoin market-data archives into StockSharp. It normalizes downloadable spot and futures data into the unified StockSharp message model.

## Key capabilities

- Instrument discovery and reference data for spot and futures markets.
- Historical tick trades, order books, and time-frame candles.
- Date-range downloads for repeatable backfilling of market-data storage.
- Exchange symbols and market segments are mapped to StockSharp security identifiers.
- This adapter is intended for historical data and does not provide live subscriptions or order routing.
- KuCoin archive transport and file formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector to prepare KuCoin histories for charting, analytics, market replay, and strategy backtesting.

Available instruments, files, dates, depths, and candle intervals depend on the public datasets retained by KuCoin.
