# Bybit Historical Data Connector

The **Bybit Historical Data connector** imports public Bybit market-data archives into StockSharp. It normalizes downloadable exchange data for spot and derivative instruments into the standard StockSharp message model.

## Key capabilities

- Instrument discovery for spot, linear, inverse, and options markets.
- Historical tick trades for supported spot and derivative instruments.
- Historical incremental order-book data for supported markets and depths.
- Date-range downloads suitable for bulk backfilling and repeatable research datasets.
- This adapter is intended for historical data and does not provide live subscriptions or order routing.
- Bybit archive formats and market identifiers are hidden behind the standard StockSharp API.

## Typical use

Use this connector to build trade and order-book histories for analytics, market replay, and strategy backtesting.

Available instruments, dates, order-book depths, and files depend on the public datasets retained by Bybit.
