# CoinCap Connector for StockSharp

This directory contains the **CoinCap** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `CoinCapMessageAdapter` message adapter, exposing CoinCap market data through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: tick trades, Level1 (best bid/ask and last trade).
- Real-time streaming over a WebSocket connection.
- Market-data only connector (no order routing).
- Trading board code: `CoinCap`.

## Configuration

`CoinCapMessageAdapter` is configured through the following properties:

- `Token` – Personal API token.
- `Address` – Connection endpoint address.

## Usage

```csharp
var adapter = new CoinCapMessageAdapter(new IncrementalIdGenerator())
{
    Token = "YOUR_TOKEN".ToSecureString(),
    Address = "...",
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data through the StockSharp API.

## Documentation

See the [CoinCap connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/coincap.html).
