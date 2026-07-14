# Alpaca Connector for StockSharp

This directory contains the **Alpaca** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `AlpacaMessageAdapter` message adapter, exposing Alpaca market data and trading operations through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: Level1 (best bid/ask and last trade), tick trades, news, order book (market depth).
- Real-time streaming over a WebSocket connection.
- Order registration, replacement and cancellation through the standard StockSharp transactional model.

## Configuration

`AlpacaMessageAdapter` is configured through the following properties:

- `Key` – API key.
- `Secret` – API secret used to sign requests.
- `IsDemo` – Set to `true` to use the demo/sandbox environment.

## Usage

```csharp
var adapter = new AlpacaMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_KEY".ToSecureString(),
    Secret = "YOUR_SECRET".ToSecureString(),
    IsDemo = false,
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data and send orders through the StockSharp API.

## Documentation

See the StockSharp guide on [creating your own connector](https://doc.stocksharp.com/en/topics/api/connectors/creating_own_connector.html).
