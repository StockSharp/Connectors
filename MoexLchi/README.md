# MoexLchi Connector for StockSharp

This directory contains the **MoexLchi** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `MoexLchiMessageAdapter` message adapter, exposing MoexLchi market data through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: order log.
- Data access over HTTP/REST.
- Market-data only connector (no order routing).
- Trading board code: `Forts`.

## Usage

```csharp
var adapter = new MoexLchiMessageAdapter(new IncrementalIdGenerator());
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data through the StockSharp API.

## Documentation

See the StockSharp guide on [creating your own connector](https://doc.stocksharp.com/en/topics/api/connectors/creating_own_connector.html).
