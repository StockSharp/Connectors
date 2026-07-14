# PolygonIO Connector for StockSharp

This directory contains the **PolygonIO** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `PolygonIOMessageAdapter` message adapter, exposing PolygonIO market data through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: Level1 (best bid/ask and last trade), news, tick trades.
- Real-time streaming over a WebSocket connection.
- Market-data only connector (no order routing).
- Trading board code: `StockSharp`.

## Configuration

`PolygonIOMessageAdapter` is configured through the following properties:

- `Key` – API key.
- `Secret` – API secret used to sign requests.
- `Token` – Personal API token.
- `Address` – Connection endpoint address.

## Usage

```csharp
var adapter = new PolygonIOMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_KEY".ToSecureString(),
    Secret = "YOUR_SECRET".ToSecureString(),
    Token = "YOUR_TOKEN".ToSecureString(),
    Address = "...",
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data through the StockSharp API.

## Documentation

See the [PolygonIO connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/polygonio.html).
