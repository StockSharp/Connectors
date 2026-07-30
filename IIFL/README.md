# IIFL Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **IIFL connector** connects StockSharp to the IIFL Markets Open API for Indian exchange market data and brokerage operations. It maps IIFL REST and MQTT services to the standard StockSharp message model.

## Key capabilities

- Instrument discovery across NSE, BSE, equity-derivatives, currency-derivatives, and commodity segments, including stocks, indices, futures, and options.
- Level 1 snapshots, five-level order books, and live tick updates through REST and the official MQTT stream.
- Historical candles and polling updates for 1, 5, 10, 15, and 30 minutes; 1 hour; 1 day; 1 week; and 1 month.
- Market, limit, stop-loss, and stop-loss-market orders with modification and individual cancellation; bulk cancellation is not supported.
- IIFL-specific products and order complexities, trigger prices, disclosed volume, market protection, algorithm identifiers, and client tags.
- Portfolio funds, holdings, positions, order status, and executions through REST snapshots and private MQTT updates.
- Configurable MQTT streaming and REST polling for private state and active candles.

## Typical use

Use this connector for Indian-market trading terminals, live strategies, order-management services, portfolio monitoring, and candle-based analysis.

Connection requires IIFL application credentials, a client identifier, and either the daily authorization flow or an existing session token. Instruments, market-data permissions, order features, trading hours, and request limits depend on the IIFL account and exchange segment.
