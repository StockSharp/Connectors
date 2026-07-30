# Settrade Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Settrade connector** connects StockSharp to Settrade Open API v2 for Thai equities and derivatives. It unifies the provider's REST and MQTT market-data and brokerage services under the StockSharp message model.

## Key capabilities

- Direct symbol lookup for the configured SET equity or TFEX derivatives account; bulk instrument catalogue download is not provided.
- Real-time Level 1 quotes and order-book snapshots and updates; tick-trade subscriptions are not exposed.
- Historical time-frame candles followed by MQTT candle updates for supported intervals.
- Market and limit orders, plus supported TFEX conditional orders; equity stop orders are not exposed.
- Order modification and cancellation with Settrade-specific validity, NVDR, iceberg, position, and trigger fields where applicable.
- Account information, portfolios, positions, orders, and trades through snapshots, private topics, and periodic reconciliation.
- Production and sandbox endpoints are configurable; credentials, broker ID, account, account type, and trading PIN are required as applicable.
- Settrade authentication, MQTT topics, and payloads are hidden behind the standard StockSharp API.

## Typical use

Use this connector for Thai-market trading terminals, live strategies, order-management services, and account-monitoring tools connected through Settrade.

Available symbols, candle intervals, market depth, account functions, trading permissions, and request limits are controlled by Settrade, the selected equity or derivatives account, and its entitlements.
