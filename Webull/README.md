# Webull Connector for StockSharp

This directory contains the Webull OpenAPI connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements all three Webull transports: HTTP for requests and subscription control, MQTT/TLS for live market data, and gRPC for trade events.

## Features

- Streaming Level1 snapshots and best bid/ask updates.
- Streaming market depth and tick trades.
- Security lookup, account balances and positions.
- Stock order registration and cancellation.
- Real-time order-status events through gRPC.
- Production and sandbox environments for HTTP, MQTT and gRPC.

## Configuration

- `Key` and `Secret` — Webull OpenAPI application credentials.
- `Token` — optional reusable access token when account verification requires it.
- `Account` — trading account identifier; when omitted, accounts are requested from Webull.
- `IsDemo` — enables the Webull sandbox HTTP environment.

## Usage

```csharp
var adapter = new WebullMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_APP_KEY".ToSecureString(),
    Secret = "YOUR_APP_SECRET".ToSecureString(),
    Token = "YOUR_ACCESS_TOKEN".ToSecureString(),
    Account = "YOUR_ACCOUNT_ID",
};
```

## Documentation

- [StockSharp Webull connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/webull.html)
- [Official Webull OpenAPI documentation](https://developer.webull.com/apis/docs/)
- [Webull OpenAPI protocols](https://developer.webull.com/apis/docs/about-open-api/)
- [Webull SDKs, endpoints and environments](https://developer.webull.com/apis/docs/sdk/)
