# Binance Historical Data Connector

The **Binance Historical Data connector** imports public Binance market-data archives into StockSharp. It converts exchange files and reference data into the unified StockSharp message model for consistent storage, analysis, and replay.

## Key capabilities

- Coverage for digital-asset spot and derivatives markets.
- Instrument discovery and contract reference data.
- Historical Level 1 quotes, tick trades, order books, and time-frame candles.
- Date-range downloads suitable for automated market-data backfilling.
- This adapter is intended for historical data and does not provide live subscriptions or order routing.
- Binance archive formats and identifiers are normalized behind the standard StockSharp API.

## Typical use

Use this connector to populate local storage, repair gaps in historical series, and prepare data for research and strategy backtesting.

Available instruments, files, date ranges, and data granularity depend on the archives published by Binance.
