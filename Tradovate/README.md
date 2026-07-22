# Tradovate Connector for StockSharp

This directory contains the Tradovate connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It uses the official Tradovate REST and WebSocket APIs.

## Features

- Futures contract lookup with maturity, exchange, tick-size and multiplier metadata.
- Real-time quotes, trades and depth of market over WebSocket.
- Historical and real-time time-frame candles.
- Accounts, cash balances, positions, orders and fills.
- Order registration, modification and cancellation.
- Live and demo environments.

## Configuration

- `Login` — Tradovate user name.
- `Password` — Tradovate password.
- `Key` — API key client identifier (`cid`).
- `Secret` — API key secret (`sec`).
- `AppId` and `AppVersion` — application identity registered with Tradovate.
- `DeviceId` — stable device identifier registered for API access.
- `IsDemo` — use the simulation environment.

## Documentation

- [StockSharp Tradovate connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/tradovate.html)
- [Official Tradovate API documentation](https://api.tradovate.com/)
- [Official Tradovate API examples](https://github.com/tradovate/example-api-js)
