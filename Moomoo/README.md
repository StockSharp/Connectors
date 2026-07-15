# Moomoo Connector for StockSharp

This directory contains the Moomoo OpenAPI connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It communicates with Moomoo OpenD over its official TCP protocol and uses the official protobuf-based .NET SDK.

## Features

- US stocks, ETFs, options, indexes, bonds, and supported crypto instruments.
- Security lookup and option contract metadata.
- Real-time Level 1 quotes, market depth, and tick trades.
- Historical and live one-minute through monthly candles.
- Simulated and live brokerage accounts, balances, and positions.
- Current and historical orders and fills.
- Market, limit, stop, and stop-limit orders.
- Order replacement and cancellation.
- Regular, extended-hours, all-session, and overnight order sessions.

## Configuration

Install and start Moomoo OpenD before connecting. By default, the connector uses `127.0.0.1:11111`, the standard local OpenD endpoint.

- `Address` - Moomoo OpenD host and TCP port.
- `Password` - optional OpenD trading password. The connector sends the MD5 digest required by the official unlock-trade protocol.
- `IsDemo` - selects simulated accounts when enabled and live accounts when disabled.

Market-data availability and subscription limits depend on the quote entitlements of the Moomoo account logged into OpenD. Live trading also requires OpenD trading access to be enabled for the account.

## Documentation

- [StockSharp Moomoo connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/moomoo.html)
- [Official Moomoo OpenAPI documentation](https://openapi.moomoo.com/moomoo-api-doc/en/intro/intro.html)
- [Quote API overview](https://openapi.moomoo.com/moomoo-api-doc/en/quote/overview.html)
- [Moomoo OpenD download](https://www.moomoo.com/download/OpenAPI)
- [Official .NET SDK package](https://www.nuget.org/packages/moomoo-api)
