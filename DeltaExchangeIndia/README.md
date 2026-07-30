# Delta Exchange India Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Delta Exchange India connector** connects StockSharp to an Indian centralized digital-asset derivatives venue. It maps futures and options market data, orders, and account state to the standard StockSharp message model.

## Key capabilities

- Discovery and reference data for the futures and options listed by Delta Exchange India.
- REST snapshots and live WebSocket updates for Level 1 quotes; historical Level 1 events are not available.
- Recent tick history through REST, limited to 50 trades per request, plus live WebSocket trades.
- Order-book snapshots and live updates with up to 15 levels; incremental and historical books are not supported.
- Historical candles, up to 1,999 bars per request, and live candle updates for provider-supported intervals.
- Limit, market, and conditional stop orders, including post-only and reduce-only flags, replacement, cancellation, and bulk cancellation.
- Portfolio, balance, position, order, and fill updates through authenticated REST and private streams.

## Typical use

Use this connector for live derivatives strategies, trading terminals, order-management services, and analysis that needs recent trades or candle history from Delta Exchange India.

Private operations require API credentials and the necessary account permissions. Instrument access, history windows, rate limits, and regional availability are controlled by the provider; iceberg orders, absolute order expiry, and bulk position closing are not implemented.
