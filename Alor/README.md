# Alor Connector for StockSharp

This directory contains the **Alor** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `AlorMessageAdapter` message adapter, exposing Alor market data and trading operations through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: order book (market depth), Level1 (best bid/ask and last trade), tick trades.
- Real-time streaming over a WebSocket connection.
- Order registration, replacement and cancellation through the standard StockSharp transactional model.
- Trading board code: `Moex`.

## Configuration

`AlorMessageAdapter` is configured through the following properties:

- `Token` – Personal API token.
- `IsDemo` – Set to `true` to use the demo/sandbox environment.

## Usage

```csharp
var adapter = new AlorMessageAdapter(new IncrementalIdGenerator())
{
    Token = "YOUR_TOKEN".ToSecureString(),
    IsDemo = false,
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data and send orders through the StockSharp API.

## Documentation

See the StockSharp guide on [creating your own connector](https://doc.stocksharp.com/en/topics/api/connectors/creating_own_connector.html).
