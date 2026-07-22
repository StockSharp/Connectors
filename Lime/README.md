# Lime Trader Connector for StockSharp

This directory contains the Lime Trader connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It uses the official Lime Trader REST API and account streaming feed.

## Features

- OAuth password-flow authentication with an application client ID and secret.
- US stock and option symbol lookup.
- Current quote snapshots, including option Greeks when supplied by Lime.
- Historical minute, hourly, daily, and weekly candles.
- Account balances, buying power, positions, active orders, and current-day trades.
- Real-time balance, position, order, and trade updates over the account WebSocket.
- Market and limit order registration and order cancellation.

Lime Trader does not provide a public market-data WebSocket. Quote subscriptions therefore return REST snapshots, while account and execution updates use the official WebSocket feed.

## Configuration

- `Login` — Lime user name.
- `Password` — Lime password.
- `Key` — OAuth client identifier issued for the application.
- `Secret` — OAuth client secret issued for the application.

API access, real-time data, options trading, and order routes depend on the permissions and market-data entitlements assigned to the Lime account.

## Documentation

- [StockSharp Lime Trader connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/lime.html)
- [Official Lime Trader API](https://docs.lime.co/trader/)
- [Authentication](https://docs.lime.co/trader/authentication/password-flow/)
- [Account streaming feed](https://docs.lime.co/trader/accounts/streaming-feed/)
- [Market data](https://docs.lime.co/trader/market-data/)
- [Trading](https://docs.lime.co/trader/trading/)
