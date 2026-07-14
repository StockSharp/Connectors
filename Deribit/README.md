# Deribit Connector for StockSharp

This directory contains the **Deribit** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `DeribitMessageAdapter` message adapter, exposing Deribit market data and trading operations through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: tick trades, order book (market depth), Level1 (best bid/ask and last trade), news.
- Real-time streaming over a WebSocket connection.
- Order registration, replacement and cancellation through the standard StockSharp transactional model.
- Trading board code: `Deribit`.

## Configuration

`DeribitMessageAdapter` is configured through the following properties:

- `Key` – API key.
- `Secret` – API secret used to sign requests.
- `Address` – Connection endpoint address.
- `IsDemo` – Set to `true` to use the demo/sandbox environment.

## Usage

```csharp
var adapter = new DeribitMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_KEY".ToSecureString(),
    Secret = "YOUR_SECRET".ToSecureString(),
    Address = "...",
    IsDemo = false,
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data and send orders through the StockSharp API.

## Documentation

See the [Deribit connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/deribit.html).
