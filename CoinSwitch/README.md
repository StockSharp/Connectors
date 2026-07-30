# CoinSwitch Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **CoinSwitch connector** integrates StockSharp with the CoinSwitch PRO APIs. A product setting selects INR or USDT spot markets, USDT-margined perpetual futures, or the private-beta HFT options surface.

## Key capabilities

- Discover instruments for the selected CoinSwitch product.
- Subscribe to Level 1 quotes, market depth, tick trades, and time-frame candles.
- Download candle history and receive live updates by WebSocket where the selected product and interval support them.
- Submit spot limit orders, futures limit, market, or stop-market orders, and HFT options limit or market orders.
- Use reduce-only for supported derivatives orders and supported time-in-force modes for HFT options.
- Cancel individual or matching groups of orders.
- Load balances, positions, open and historical orders, and own trades.

## Typical use

Use this connector for CoinSwitch PRO market monitoring and automated trading across one selected product surface. Private operations require an API key and Ed25519 secret with suitable permissions; options also require access to the CoinSwitch HFT private beta.

Capabilities vary by product: spot order entry is limit-only, conditional entry is implemented only for futures as stop-market, and options candles do not use WebSocket streaming. Atomic replacement, iceberg and GTD orders, incremental order books, and an order-log feed are not supported. CoinSwitch permissions and rate limits apply.
