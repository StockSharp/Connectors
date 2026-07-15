# TradeZero Connector for StockSharp

This directory contains the TradeZero connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It uses the official TradeZero REST API and both official account WebSocket streams.

## Features

- Equity and single-leg option order registration and cancellation.
- Cancel-and-replace order workflow (TradeZero has no modify endpoint).
- Accounts, balances, open positions, current orders and execution updates.
- Real-time order and position updates over the Portfolio WebSocket stream.
- Real-time account and position P&L over the P&L WebSocket stream.
- Security search plus snapshot quotes, market depth and historical candles over REST.
- Paper and live accounts through the same API host; credentials select the environment.

TradeZero does not currently document a public market-data WebSocket. Market quotes, depth and candles are therefore exposed as finite REST snapshots, while account data uses the documented WebSocket streams.

## Configuration

- `Key` — `TZ-API-KEY-ID` generated in the TradeZero portal.
- `Secret` — `TZ-API-SECRET-KEY` generated in the TradeZero portal.
- `DefaultRoute` — optional preferred account route returned by `GET /v1/api/accounts/{accountId}/routes`. If it is empty, the connector selects a compatible live route from that endpoint.

## Documentation

- [StockSharp TradeZero connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/tradezero.html)
- [Official TradeZero developer documentation](https://developer.tradezero.com/docs/documentation)
- [Official Trading API documentation](https://developer.tradezero.com/docs/documentation/trading)
- [Official WebSocket API documentation](https://developer.tradezero.com/docs/websocket_api)
