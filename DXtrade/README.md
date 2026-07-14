# DXtrade Connector for StockSharp

This directory contains the **DXtrade** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `DXtradeMessageAdapter` message adapter, exposing DXtrade market data and trading operations through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: Level1 (best bid/ask and last trade).
- Real-time streaming over a WebSocket connection.
- Order registration, replacement and cancellation through the standard StockSharp transactional model.
- Trading board code: `DevExperts`.

## Configuration

`DXtradeMessageAdapter` is configured through the following properties:

- `Password` – Account password.
- `Login` – Account login.
- `Address` – Connection endpoint address.
- `IsDemo` – Set to `true` to use the demo/sandbox environment.

## Usage

```csharp
var adapter = new DXtradeMessageAdapter(new IncrementalIdGenerator())
{
    Password = "YOUR_PASSWORD".ToSecureString(),
    Login = "...",
    Address = "...",
    IsDemo = false,
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data and send orders through the StockSharp API.

## Documentation

See the [DXtrade connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/forex/dxtrade.html).
