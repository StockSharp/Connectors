# Tradier Connector for StockSharp

This directory contains the Tradier connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It combines the Tradier Brokerage REST API with real-time streaming support.

## Features

- Security lookup and option-chain data.
- Level1, tick trades, candles and market depth.
- Account balances, positions and order status.
- Order registration, replacement and cancellation.
- Production and sandbox environments.

## Configuration

- `Token` — Tradier API access token.
- `IsDemo` — enables the Tradier sandbox environment.

## Usage

```csharp
var adapter = new TradierMessageAdapter(new IncrementalIdGenerator())
{
    Token = "YOUR_ACCESS_TOKEN".ToSecureString(),
    IsDemo = true,
};
```

## Documentation

- [StockSharp Tradier connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/tradier.html)
- [Official Tradier Brokerage API](https://documentation.tradier.com/brokerage-api)
