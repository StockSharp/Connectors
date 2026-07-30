# Finage Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Finage connector** connects StockSharp to Finage's Forex market-data services. It is a read-only adapter for currency instruments, combining REST reference and historical data with an optional WebSocket quote stream.

## Key capabilities

- Currency-pair discovery from a configured symbol list or the provider's REST symbol search.
- Current bid and ask snapshots through REST.
- Live Level 1 bid and ask updates through WebSocket when a separate streaming token is configured.
- Historical time-frame candles through REST for 1, 5, 10, 15, and 30 minutes; 1, 2, 4, 6, 8, and 12 hours; 1 day; and 1 week.
- Configurable request interval and maximum instrument count for controlling REST usage.
- Historical Level 1 events and live candle updates are not supported.
- No tick trades, order books, order entry, portfolio data, or account operations.

## Typical use

Use this connector for Forex watchlists, quote monitoring, charting, research, and backtesting based on Finage candle history.

A Finage REST API key is required, and live quotes additionally require a streaming token. Symbol coverage, history depth, real-time access, and request limits depend on the subscribed Finage plan.
