# TradeOgre Connector for StockSharp

This directory contains the **TradeOgre** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `TradeOgreMessageAdapter` message adapter, exposing TradeOgre market data and trading operations through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: tick trades, Level1 (best bid/ask and last trade), order book (market depth).
- Data access over HTTP/REST.
- Order registration, replacement and cancellation through the standard StockSharp transactional model.
- Trading board code: `TradeOgre`.

## Configuration

`TradeOgreMessageAdapter` is configured through the following properties:

- `Key` – API key.
- `Secret` – API secret used to sign requests.

## Usage

```csharp
var adapter = new TradeOgreMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_KEY".ToSecureString(),
    Secret = "YOUR_SECRET".ToSecureString(),
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data and send orders through the StockSharp API.

## Documentation

See the [TradeOgre connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/tradeogre.html).
