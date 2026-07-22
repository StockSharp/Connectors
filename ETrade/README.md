# E*TRADE Connector for StockSharp

This directory contains the E*TRADE Developer Platform connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It uses the E*TRADE REST API and OAuth 1.0a authentication.

## Features

- Equity security lookup and Level1 snapshots.
- Account balances and positions.
- Equity order registration and cancellation.
- Production and sandbox environments.

## Configuration

- `Key` and `Secret` — OAuth consumer credentials.
- `AccessToken` and `AccessSecret` — authorized user-session credentials.
- `IsDemo` — enables the sandbox API host.

## Usage

```csharp
var adapter = new ETradeMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_CONSUMER_KEY".ToSecureString(),
    Secret = "YOUR_CONSUMER_SECRET".ToSecureString(),
    AccessToken = "YOUR_ACCESS_TOKEN".ToSecureString(),
    AccessSecret = "YOUR_ACCESS_SECRET".ToSecureString(),
    IsDemo = true,
};
```

## Documentation

- [StockSharp E*TRADE connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/e_trade.html)
- [Official E*TRADE getting started guide](https://developer.etrade.com/getting-started)
- [E*TRADE OAuth and sandbox guide](https://developer.etrade.com/getting-started/developer-guides)
