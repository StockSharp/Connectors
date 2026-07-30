# CoinSpot Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **CoinSpot connector** integrates StockSharp with the CoinSpot cryptocurrency spot exchange and broker. It uses CoinSpot's public, trading, and read-only REST APIs for market data, account state, and order operations.

## Key capabilities

- Discover CoinSpot spot markets and instrument metadata.
- Request Level 1 ticker snapshots, order-book snapshots, and recent tick trades.
- Keep public subscriptions updated through configurable REST polling.
- Submit limit and market buy or sell orders.
- Cancel individual orders or groups of matching open orders.
- Load balances, portfolio state, open and historical orders, and own trades.
- Configure separate public, trading, and read-only API endpoints.

## Typical use

Use this connector for CoinSpot spot-market monitoring and REST-based automated trading. Public market data can be used without authentication; account and order operations require a CoinSpot API key and secret with the appropriate permissions.

The adapter has no WebSocket stream and does not provide candles, historical Level 1 events, or historical order books. Public updates are polling based, and recent-trade history is limited by the provider response. Atomic replacement, conditional, iceberg, post-only, and GTD orders are not supported.
