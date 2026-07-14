# cTrader Connector for StockSharp

This directory contains the **cTrader** connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It implements the `cTraderMessageAdapter` message adapter, exposing cTrader market data and trading operations through the StockSharp message model. The source can be used as a reference for building your own connector or included directly in a StockSharp-based application.

## Features

- Market data: order book (market depth), Level1 (best bid/ask and last trade).
- Data access over HTTP/REST.
- Order registration, replacement and cancellation through the standard StockSharp transactional model.
- Trading board code: `cTrader`.

## Configuration

`cTraderMessageAdapter` is configured through the following properties:

- `ClientSecret` – Client secret.
- `ClientId` – Client identifier.
- `Address` – Connection endpoint address.
- `IsDemo` – Set to `true` to use the demo/sandbox environment.

## Usage

```csharp
var adapter = new cTraderMessageAdapter(new IncrementalIdGenerator())
{
    ClientSecret = "YOUR_CLIENTSECRET".ToSecureString(),
    ClientId = "...",
    Address = "...",
    IsDemo = false,
};
```

Add the adapter to a `Connector` (or another component that consumes message adapters) and connect as usual; then subscribe to market data and send orders through the StockSharp API.

## Documentation

See the [cTrader connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/forex/ctrader.html).
