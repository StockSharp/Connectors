# AlphaVantage Connector for StockSharp

This directory contains the **AlphaVantage** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `AlphaVantageMessageAdapter` message adapter, exposing AlphaVantage market data through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Data access over HTTP/REST.
- Market-data only connector (no order routing).
- Trading board code: `AlphaVantage`.

## Configuration

`AlphaVantageMessageAdapter` is configured through the following properties:

- `Token` – Personal API token.

## Usage

```csharp
var adapter = new AlphaVantageMessageAdapter(new IncrementalIdGenerator())
{
    Token = "YOUR_TOKEN".ToSecureString(),
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data through the StockSharp API.

## Documentation

See the [AlphaVantage connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/alphavantage.html).
