# Gate.io Historical Data Connector

The **Gate.io Historical Data connector** imports public Gate.io market-data archives into StockSharp. It converts spot and derivatives datasets into the unified StockSharp message model for storage, analysis, and replay.

## Key capabilities

- Instrument discovery for spot, perpetual-futures, and delivery-futures markets.
- Historical tick trades, incremental order books, and time-frame candles.
- Date-range downloads for systematic market-data backfilling.
- Native symbols and market variants are mapped to StockSharp security identifiers.
- This adapter is intended for historical data and does not provide live subscriptions or order routing.
- Gate.io archive formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector to prepare crypto histories for charting, analytics, order-book research, and strategy backtesting.

Available instruments, files, dates, depths, and candle intervals depend on the public datasets published by Gate.io.
