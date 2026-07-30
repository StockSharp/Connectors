# Birdeye Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Birdeye connector** integrates StockSharp with Birdeye's on-chain cryptocurrency data APIs. It provides token discovery, current market indicators, and OHLCV history for a selected blockchain, with Solana used by default.

## Key capabilities

- Discover tokens and load reference data for a selected chain.
- Narrow discovery to a token address and apply a minimum-liquidity filter.
- Request Level 1 snapshots and receive updated values through REST polling.
- Download historical time-frame candles, subject to the configured history limit.
- Enable paid WebSocket streaming for live Level 1 and candle updates.
- Express prices in USD or in the chain's native currency.
- Use Birdeye-supported intervals; sub-minute candles are available only for Solana.

## Typical use

Use this connector for token screening, on-chain price monitoring, and historical OHLCV analysis across Birdeye-supported networks. Configure the chain, API token, quote mode, and optional discovery filters before subscribing.

Birdeye is a market-data provider, so this connector does not expose orders, portfolios, trades, or an order book. Historical Level 1 events are not available, and without streaming enabled a candle subscription ends after the historical response. Data coverage, WebSocket access, and request limits depend on the Birdeye API plan.
