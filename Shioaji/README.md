# SinoPac Shioaji connector

This directory contains the StockSharp connector for the official cross-language Shioaji HTTP server introduced in Shioaji 1.5 and expanded by Contract V2 in Shioaji 1.7.

## Runtime dependency

Install the official Shioaji CLI/standalone package, configure its `.env`, and start `shioaji server start` for simulation or `shioaji server start --production` for live trading. The connector talks directly to that supported REST/OpenAPI and Server-Sent Events interface; it does not launch Python, embed a Python runtime, or reverse-engineer the upstream Solace transport.

The default server address is `http://localhost:8080/`. Localhost requests require no HTTP authentication. For a remotely exposed server, configure the Shioaji API key and secret so the connector sends the documented `Authorization: Bearer API_KEY:SECRET_KEY` header. Protect remote access with network controls; Shioaji's server contains trading and account endpoints.

## Coverage

- Contract V2 lookup for Taiwan stocks, indices, warrants, futures, and options.
- REST snapshots, historical ticks, and one-minute K-bars. Long K-bar requests are split into windows below the official 30-day limit.
- Official SSE streams for trades, Level1 quotes, five-level books, index values, and order/deal events, with heartbeat-aware reconnect and subscription recovery.
- Stock, warrant, futures, and option order placement; price/quantity modification; cancellation; order reconciliation; accounts, balances, margin, and positions.

Shioaji provides native one-minute historical K-bars but no candle SSE channel. Realtime candles should be built from the tick stream with StockSharp aggregation. The official server must already be logged in; live orders additionally require certificate activation. Simulation accounting endpoints intentionally return zero/empty values where documented by SinoPac.

## Official references

- [Shioaji repository and cross-language server](https://github.com/Sinotrade/Shioaji)
- [Cross-language setup](https://sinotrade.github.io/env_setup/other/)
- [HTTP API and OpenAPI](https://sinotrade.github.io/quickstart/)
- [Contract V2](https://sinotrade.github.io/tutor/contract/)
- [Streaming market data](https://sinotrade.github.io/tutor/market_data/streaming/)
- [Orders](https://sinotrade.github.io/tutor/order/Stock/)
- [Accounting](https://sinotrade.github.io/tutor/accounting/position/)
