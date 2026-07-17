# Trading Technologies Connector for StockSharp

This connector integrates StockSharp with the official client-side TT .NET SDK. The connector does not redistribute `tt-net-api.dll`, TT credentials, or market data.

## Features

- Instrument search and exact instrument lookup.
- Real-time Level1 fields, aggregated market depth, and time-and-sales subscriptions.
- TT account, order, fill, position, and P&L snapshots and live updates.
- Order registration, modification, and cancellation.
- Production-live, production-simulation, and UAT-certification environments.
- Native TT Edge WebSocket connectivity, recovery, and resynchronization managed by the official SDK.

## Requirements

- A TT user with the required market-data and trading permissions.
- A TT application key and secret, represented by the combined `GUID:GUID` app-secret string required by the TT .NET SDK.
- The official TT .NET SDK and its matching runtime dependencies.

Install the client-side TT .NET SDK, then set `SdkPath` to `tt-net-api.dll` or its containing directory. The library is loaded at runtime so a proprietary SDK binary is never included in this package.

Only one TT SDK session should be hosted in a process. `TTAPI.Shutdown` is global to the client-side SDK.

## Configuration

- `SdkPath` — path to `tt-net-api.dll` or its containing directory.
- `AppSecretKey` — combined TT application key and secret.
- `Environment` — `ProdLive`, `ProdSim`, or `UatCert`.
- `InitializationTimeout` — SDK initialization timeout in milliseconds.
- `MarketDepth` — maximum number of aggregated order-book levels.
- `IsBinaryProtocol` — enables the TT binary protocol.
- `IsOptionsEnabled` — enables options market data in the SDK session.

The connector intentionally advertises real-time data only. The client-side TT .NET SDK market-data subscriptions do not provide candle or historical-tick downloads; use a separate historical source when those data types are required. Instrument visibility, prices, and trading operations are always limited by the connected TT user's entitlements.

## Documentation

- [StockSharp Trading Technologies connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/trading_technologies.html)
- [Official TT APIs overview](https://library.tradingtechnologies.com/apis/tt-apis/)
- [Official TT .NET SDK guide](https://library.tradingtechnologies.com/tt-net-sdk/)
- [TT .NET SDK price subscriptions](https://library.tradingtechnologies.com/tt-net-sdk/articles/md-price-sub.html)
- [TT .NET SDK time-and-sales subscriptions](https://library.tradingtechnologies.com/tt-net-sdk/articles/md-ts-sub.html)
- [TT .NET SDK order submission](https://library.tradingtechnologies.com/apis/tt-net-sdk/working-with-orders-and-fills-tt-net-sdk/submitting-orders-2/)
