# TraderMade Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **TraderMade connector** connects StockSharp to TraderMade's foreign-exchange and cryptocurrency market-data services. It maps REST history and WebSocket quotes to the unified StockSharp market-data model.

## Key capabilities

- Pair discovery from the provider's currency list and configured quote currencies, or from an explicit symbol list.
- Real-time Level 1 bid, ask, and mid-price updates through the streaming API.
- Optional TraderLadder order-book data when ladder access is enabled for the account.
- Historical time-frame candles through REST, including optional weekend cryptocurrency data.
- Separate REST and streaming credentials allow history-only, streaming-only, or combined configurations.
- Candle subscriptions are finite historical requests; live candle updates and tick-trade subscriptions are not supported.
- This is a market-data-only connector with no portfolios, balances, or order-entry operations.
- TraderMade symbols, transports, and response formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector for FX and cryptocurrency dashboards, live quote monitoring, charting, analytics, and historical backtests that do not require broker execution.

Available pairs, ladder depth, candle intervals, history, rate limits, weekend data, and streaming access are controlled by TraderMade and the connected API plan.
