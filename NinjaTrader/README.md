# NinjaTrader Connector for StockSharp

This directory contains the NinjaTrader connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It uses the official NinjaTrader REST and WebSocket APIs.

## Features

- Futures contract lookup with maturity, exchange, tick-size, and multiplier metadata.
- Real-time quotes, trades, and depth of market over the market-data WebSocket.
- Historical and real-time time-frame candles.
- Accounts, cash balances, positions, orders, and fills.
- Real-time order, fill, position, and balance updates over the trading WebSocket.
- Order registration, modification, and cancellation.
- Live and demo environments with their dedicated trading and market-data endpoints.

## Configuration

- `Login` — NinjaTrader user name.
- `Password` — NinjaTrader password.
- `ClientId` — API key client identifier (`cid`).
- `Secret` — API key secret (`sec`).
- `AppId` and `AppVersion` — registered application identity.
- `DeviceId` — stable device identifier registered for API access.
- `IsDemo` — use the simulation environment.

API access and real-time market data depend on the permissions and exchange entitlements assigned to the NinjaTrader account and API key.

## Documentation

- [StockSharp NinjaTrader connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/ninjatrader.html)
- [Official NinjaTrader API documentation](https://docs.ninjatrader.com/api)
- [Official WebSocket protocol](https://docs.ninjatrader.com/api/websockets)
- [Official market-data API](https://docs.ninjatrader.com/market-data)
