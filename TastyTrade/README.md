# tastytrade Connector for StockSharp

This directory contains the tastytrade Open API connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It uses the official REST API for OAuth, reference and account data, the account WebSocket streamer for brokerage updates, and DXLink for live market data.

## Features

- Equity, equity-option, futures, futures-option and cryptocurrency instruments.
- Real-time Level1 quotes and trades over DXLink.
- Historical and live time-frame candles over DXLink time-series subscriptions.
- Brokerage accounts, balances and positions.
- Order registration, replacement and cancellation.
- Native multi-leg option and futures-option orders through `TastyTradeOrderCondition.Legs`.
- Live order, fill, balance and position updates.
- Production and sandbox environments.
- Automatic OAuth access-token refresh from the configured refresh token.

## Configuration

- `Token` — OAuth refresh token issued by tastytrade.
- `ClientSecret` — OAuth client secret associated with the refresh token.
- `Scopes` — OAuth scopes to request while refreshing (`Read`, `Trade`, or both).
- `IsDemo` — selects the tastytrade sandbox REST and account-streamer endpoints.

## Documentation

- [StockSharp tastytrade connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/tastytrade.html)
- [Official tastytrade developer documentation](https://developer.tastytrade.com/)
- [Official Open API specifications](https://developer.tastytrade.com/open-api-spec/)
- [Streaming market data](https://developer.tastytrade.com/streaming-market-data/)
- [Streaming account data](https://developer.tastytrade.com/streaming-account-data/)
- [Official JavaScript SDK](https://github.com/tastytrade/tastytrade-api-js)
