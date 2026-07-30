# CoinGlass Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **CoinGlass connector** integrates StockSharp with the CoinGlass cryptocurrency market-analytics API. It maps selected futures, spot, options, Bitcoin ETF, and Ethereum ETF datasets to StockSharp instruments, Level 1 messages, and historical candles.

## Key capabilities

- Select a CoinGlass market type and optionally restrict requests by exchange or symbol.
- Discover the instruments available for the configured dataset.
- Request current Level 1 indicators such as price, volume, change, and open interest when supplied.
- Poll Level 1 snapshots at a configurable interval.
- Download historical time-frame series for price, open interest, funding rate, or liquidation metrics.
- Apply a configurable history limit of up to 1,000 records per request.

## Typical use

Use this connector for research dashboards, derivatives monitoring, and historical analysis of CoinGlass metrics. Configure an API token, choose the market type and metric, and narrow the exchange or symbol when a focused dataset is required.

CoinGlass is an analytics source rather than an execution venue. The adapter does not provide orders, portfolios, tick trades, or market depth. Historical Level 1 events and live candle updates are not supported; candle requests return history only. Dataset availability and request limits depend on the CoinGlass subscription plan.
