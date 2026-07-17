# TradeLocker Connector for StockSharp

This connector integrates StockSharp with TradeLocker Public API v1.5. It connects to either the official demo or live REST environment selected in the adapter settings.

## Features

- JWT authentication and automatic refresh.
- Explicit account selection when a login has multiple accounts.
- Complete account instrument list and instrument metadata.
- REST-polled bid/ask Level 1 quotes.
- Historical 1-minute through weekly candles, up to the server-advertised limits.
- Balance, available funds, PnL, open positions, active orders, and order history.
- Market, limit, and stop orders; cancellation and position closing.
- Optional absolute stop-loss, take-profit, and strategy identifier fields.
- Rate-limit retry for safe requests and sequential polling workloads.

All JSON request and response bodies are represented by typed DTOs. TradeLocker's account, order, and position tables use columns published by `/trade/config`; the connector reads those rows as a JSON token stream directly into typed models and does not use a JSON DOM, `dynamic`, or dictionary-shaped protocol models.

## Configuration

- `Login` - the email used to sign in to TradeLocker.
- `Password` - the TradeLocker password.
- `Server` - the broker server name shown on the TradeLocker login form.
- `AccountId` - account ID or account name. It may be omitted only when the login exposes one account.
- `IsDemo` - selects `demo.tradelocker.com` instead of `live.tradelocker.com`.
- `DeveloperApiKey` - optional key issued through the TradeLocker Developer Program.
- `PollingInterval` - minimum interval between polling jobs; workloads rotate between quotes, orders, and portfolio data.

## Streaming limitation

The current public API documentation describes the API as request-response REST and does not publish a supported WebSocket contract. The connector therefore does not claim streaming market data or private events. Live subscriptions are implemented with rate-aware REST polling, and historical candles are finite subscriptions.

## Documentation

- [StockSharp TradeLocker connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/tradelocker.html)
- [Official TradeLocker Public API](https://public-api.tradelocker.com/)
- [Getting started and authentication](https://public-api.tradelocker.com/docs/getting-started)
- [Instrument list](https://public-api.tradelocker.com/reference/getinstruments)
- [Historical bars](https://public-api.tradelocker.com/reference/gethistory)
- [Current quotes](https://public-api.tradelocker.com/reference/getquotes)
- [Place an order](https://public-api.tradelocker.com/reference/placeorder)
