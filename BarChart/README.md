# BarChart Connector for StockSharp

This directory contains the **BarChart** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `BarChartMessageAdapter` message adapter, exposing BarChart market data through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: tick trades, Level1 (best bid/ask and last trade).
- Data access over HTTP/REST.
- Market-data only connector (no order routing).

## Configuration

`BarChartMessageAdapter` is configured through the following properties:

- `Token` – Personal API token.

## Usage

```csharp
var adapter = new BarChartMessageAdapter(new IncrementalIdGenerator())
{
    Token = "YOUR_TOKEN".ToSecureString(),
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data through the StockSharp API.

## Documentation

See the [BarChart connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/barchart.html).
