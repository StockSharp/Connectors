# AlorHistory Connector for StockSharp

This directory contains the **AlorHistory** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `AlorHistoryMessageAdapter` message adapter, exposing AlorHistory historical market data through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Data access over HTTP/REST.
- Market-data only connector (no order routing).

## Usage

```csharp
var adapter = new AlorHistoryMessageAdapter(new IncrementalIdGenerator());
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data through the StockSharp API.

## Documentation

See the StockSharp guide on [creating your own connector](https://doc.stocksharp.com/en/topics/api/connectors/creating_own_connector.html).
