# Charles Schwab Connector for StockSharp

This directory contains the Charles Schwab Trader API connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. REST is used for reference, historical and transactional requests; the Schwab streamer WebSocket is used for live data and account activity.

## Features

- Streaming Level1 equity quotes.
- Streaming NASDAQ and NYSE order books.
- Historical time-frame candles.
- Security lookup, accounts, balances and positions.
- Order registration, cancellation and status updates.

## Configuration

- `Token` — OAuth access token issued by Schwab.
- `Address` — Trader API REST address; the default is `https://api.schwabapi.com/`.

The WebSocket address and streamer identifiers are obtained automatically from the Schwab user-preferences endpoint.

## Usage

```csharp
var adapter = new SchwabMessageAdapter(new IncrementalIdGenerator())
{
    Token = "YOUR_ACCESS_TOKEN".ToSecureString(),
};
```

## Documentation

- [StockSharp Schwab connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/schwab.html)
- [Official Schwab Trader API](https://developer.schwab.com/products/trader-api--individual)
