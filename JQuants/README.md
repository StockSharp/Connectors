# J-Quants Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **J-Quants connector** connects StockSharp to the J-Quants API V2 for Japanese market reference and historical data. It is a read-only REST adapter intended for research rather than live market-data streaming or trading.

## Key capabilities

- Discovery and reference data for Japanese listed equities, futures, and options, including derivative underlyings, strikes, option types, and expiries.
- A one-shot Level 1 message synthesized from the latest available daily bar; it is not a live quote subscription.
- Historical tick trades for equities; tick history is not available for futures or options.
- Historical equity candles for 1, 5, 15, and 30 minutes; 1 hour; and 1 day.
- Historical daily candles for futures and options.
- Configurable delay between REST calls and maximum pagination depth.
- No order books, live updates, order entry, portfolio data, or account operations.

## Typical use

Use this connector for Japanese instrument catalogs, historical research, charting, data preparation, and backtesting with J-Quants datasets.

A J-Quants V2 API key is required. Available endpoints, instruments, date ranges, pagination, and request rates depend on the subscribed J-Quants plan; Level 1 values reflect a daily bar and must not be treated as real-time bid and ask data.
