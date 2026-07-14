# Interactive Brokers Connector for StockSharp

This directory contains the Interactive Brokers connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. `InteractiveBrokersMessageAdapter` connects directly to Trader Workstation (TWS) or IB Gateway through the native TWS socket protocol.

## Features

- Streaming Level1, market depth, tick-by-tick data and order updates.
- Historical market data and candles.
- Security lookup, scanners, fundamental reports and option parameters.
- Portfolio, position and account data.
- Order registration, replacement and cancellation, including advanced IB order conditions.

## Configuration

- `Address` — TWS or IB Gateway socket endpoint. Paper TWS commonly uses port `7497`; paper IB Gateway commonly uses `4002`.
- `ClientId` — API client identifier configured for the TWS session.
- TWS or IB Gateway must be running with socket clients enabled.

## Usage

```csharp
var adapter = new InteractiveBrokersMessageAdapter(new IncrementalIdGenerator())
{
    Address = new IPEndPoint(IPAddress.Loopback, 7497),
    ClientId = 1,
};
```

## Documentation

- [StockSharp Interactive Brokers connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/interactive_brokers.html)
- [Official Interactive Brokers TWS API](https://interactivebrokers.github.io/tws-api/)
- [TWS API initial setup](https://interactivebrokers.github.io/tws-api/initial_setup.html)
