# Hyperliquid Connector for StockSharp

This directory contains the **Hyperliquid** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `HyperliquidMessageAdapter` message adapter, exposing Hyperliquid market data and trading operations through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: tick trades, order book (market depth), Level1 (best bid/ask and last trade).
- Real-time streaming over a WebSocket connection.
- Order registration, replacement and cancellation through the standard StockSharp transactional model.
- Trading board code: `HyperliquidSpot`.

## Configuration

`HyperliquidMessageAdapter` is configured through the following properties:

- `Token` – Personal API token.
- `PrivateKey` – Private key used to sign requests.
- `Address` – Connection endpoint address.

## Usage

```csharp
var adapter = new HyperliquidMessageAdapter(new IncrementalIdGenerator())
{
    Token = "YOUR_TOKEN".ToSecureString(),
    PrivateKey = "YOUR_PRIVATEKEY".ToSecureString(),
    Address = "...",
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data and send orders through the StockSharp API.

## Documentation

See the [Hyperliquid connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/hyperliquid.html).
