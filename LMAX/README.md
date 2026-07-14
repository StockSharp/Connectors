# LMAX Connector for StockSharp

This directory contains the **LMAX** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `LmaxMessageAdapter` message adapter, exposing LMAX market data and trading operations through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: order book (market depth), Level1 (best bid/ask and last trade), tick trades.
- Real-time streaming over a WebSocket connection.
- Order registration, replacement and cancellation through the standard StockSharp transactional model.
- Trading board code: `Lmax`.

## Configuration

`LmaxMessageAdapter` is configured through the following properties:

- `Key` – API key.
- `Secret` – API secret used to sign requests.
- `Token` – Personal API token.
- `AccountId` – Account identifier.
- `IsDemo` – Set to `true` to use the demo/sandbox environment.

## Usage

```csharp
var adapter = new LmaxMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_KEY".ToSecureString(),
    Secret = "YOUR_SECRET".ToSecureString(),
    Token = "YOUR_TOKEN".ToSecureString(),
    AccountId = "...",
    IsDemo = false,
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data and send orders through the StockSharp API.

## Documentation

See the [LMAX connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/forex/lmax.html).
