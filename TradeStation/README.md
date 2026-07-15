# TradeStation Connector for StockSharp

This directory contains the TradeStation API v3 connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. REST endpoints are used for reference data, historical bars and order management; TradeStation HTTP streams are used for live quotes, orders and positions.

## Features

- Real-time Level1 quotes over the official HTTP streaming API.
- Historical time-frame candles.
- Symbol details for stocks, options, futures, indexes, forex and crypto pairs supported by TradeStation.
- Brokerage accounts, balances and positions.
- Order registration, replacement and cancellation.
- Live order, execution and position updates.
- Live and SIM paper-trading environments.

## Configuration

- `Token` — OAuth access token with `MarketData`, `ReadAccount` and `Trade` scopes.
- `IsDemo` — selects `https://sim-api.tradestation.com/v3` instead of the live API.
- `DefaultRoute` — order route; `Intelligent` is used by default.

TradeStation access tokens expire. The application is responsible for obtaining and refreshing the OAuth token before reconnecting the adapter.

## Documentation

- [StockSharp TradeStation connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/tradestation.html)
- [Official TradeStation API documentation](https://api.tradestation.com/docs/)
- [Official API v3 specification](https://api.tradestation.com/docs/specification/)
- [SIM and live environments](https://api.tradestation.com/docs/fundamentals/sim-vs-live/)
