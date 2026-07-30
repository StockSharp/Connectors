# CoinPaprika Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **CoinPaprika connector** integrates StockSharp with the CoinPaprika cryptocurrency market-data API. It exposes global coin reference data or markets from a selected exchange, together with ticker snapshots and historical OHLCV candles.

## Key capabilities

- Discover CoinPaprika coins globally or restrict instruments to a configured exchange.
- Choose the quote currency used for ticker and candle requests.
- Receive Level 1 snapshots with price, 24-hour volume, change, and market status when available.
- Refresh Level 1 values through configurable REST polling.
- Download historical time-frame OHLCV candles.
- Use the free API without a token or configure a token for the professional endpoint and broader entitlements.
- Limit historical responses to a configured maximum of up to 366 records.

## Typical use

Use this connector for cryptocurrency reference data, lightweight price monitoring, and historical OHLCV research. Choose global or exchange-specific discovery and set the quote currency before requesting data.

CoinPaprika is a data aggregator, not a trading venue. The adapter does not expose orders, portfolios, tick trades, or market depth. Historical Level 1 events and live candle updates are unavailable. Intraday history, coverage, response size, and rate limits depend on the CoinPaprika API plan and token.
