# Coinalyze Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Coinalyze connector** integrates StockSharp with the Coinalyze cryptocurrency market-analytics API. It maps historical price and derivatives indicators to standard StockSharp time-frame candles for futures or spot instruments.

## Key capabilities

- Select futures or spot instruments and optionally restrict discovery to an exchange.
- Download historical candles for price, open interest, funding rate, liquidation, or long/short ratio.
- Use the time frames supported by the Coinalyze API.
- Optionally convert open-interest and liquidation values to US dollars.
- Apply a configurable history limit of up to 2,000 records per request.
- Authenticate requests with a Coinalyze API token.

## Typical use

Use this connector for backtesting, derivatives research, and comparative analysis of historical Coinalyze metrics. Select the market type and candle metric before subscribing, and apply an exchange filter when the research universe must be narrowed.

This adapter is historical and REST-only. It does not provide live candle updates, Level 1 quotes, tick trades, market depth, portfolios, or order execution. Available symbols, intervals, history depth, and request rates are controlled by the Coinalyze API.
