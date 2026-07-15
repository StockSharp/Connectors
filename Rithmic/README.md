# Rithmic Connector for StockSharp

This directory contains the Rithmic protobuf/WebSocket connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Features

- Symbol search and reference data.
- Streaming tick trades, Level1 quotes and market depth.
- Futures and options market data.
- Order registration, modification and cancellation.
- Real-time order, portfolio, position and P&L updates.
- Separate Rithmic ticker, order, history and P&L plant sessions.

## Configuration

- `UserName` — Rithmic user login.
- `Password` — Rithmic user password.
- `SystemName` — Rithmic system name assigned to the account.
- `ServerAddress` — secure Rithmic WebSocket endpoint (`wss://...`).

## Documentation

- [StockSharp Rithmic connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/rithmic.html)
