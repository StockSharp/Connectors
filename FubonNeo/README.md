# Fubon Neo API Connector for StockSharp

This connector integrates StockSharp with the official Fubon Neo C# SDK for Taiwan securities and futures/options. The Fubon SDK remains a user-installed runtime dependency and is not redistributed by this repository or its NuGet package.

## Features

- Fubon account/password or API-key login with an electronic trading certificate.
- Securities and futures/options instrument lookup through the official market-data Web API.
- Historical stock candles for the intervals published by Fubon, including intraday and daily/weekly/monthly data.
- Intraday futures/options candle history.
- Official stock and futures/options WebSocket feeds for trades, five-level order books, Level1 aggregates, indices, and one-minute candles.
- Automatic WebSocket reconnect with restoration of active subscriptions.
- Securities and futures/options order registration, price/quantity modification, cancellation, current orders, and fills.
- Native order and fill callbacks from the official SDK.
- Stock balances/positions and futures/options margin, equity, and positions.
- Typed JSON transport models; reflection and native SDK objects are isolated inside the SDK bridge.

## Requirements

- A Fubon Securities/Futures account with Fubon Neo API access and the required market-data entitlements.
- An electronic trading certificate and completion of Fubon's API agreement/connectivity requirements.
- The official 64-bit C# SDK. This connector was implemented against Fubon Neo SDK 2.2.8.
- The matching native runtime for the operating system and process architecture.

Download and extract the official C# `nupkg` package. Set `SdkPath` to one of the following:

- the extracted package directory containing `lib` and `runtimes`;
- the directory containing `FubonNeo.dll` and the matching native library; or
- the full path to `FubonNeo.dll`.

When an extracted package root is supplied, the connector locates `lib/net6.0/FubonNeo.dll` (or the compatible .NET Standard assembly) and the matching `runtimes/<rid>/native` library. Vendor binaries must not be copied into this repository.

## Configuration

- `PersonalId` — personal identifier registered with Fubon.
- `Password` — trading password for standard login.
- `ApiKey` and `IsApiKeyLogin` — API-key login, supported by official SDK versions 2.2.7 and later.
- `CertificatePath` and `CertificatePassword` — electronic trading certificate credentials.
- `EnvironmentUrl` — leave empty for production. For testing, use the URL supplied in Fubon's official test-environment package; Fubon currently documents test accounts for securities only.
- `RealtimeMode` — `Normal` supports every advertised channel. `Speed` minimizes latency but the official SDK rejects aggregate and candle subscriptions.
- `ReconnectAttempts` — maximum attempts for each interrupted official WebSocket connection.

Fubon order quantities are whole shares/lots. `FubonNeoOrderCondition` exposes securities session and financing type, futures/options position effect, after-hours routing, native price type, and the optional `UserTag`. SDK 2.2.8 accepts only ASCII letters and digits in `UserTag`, up to ten characters.

Market-data availability, rate limits, products, short selling, margin trading, after-hours trading, and order types depend on the connected account and Fubon permissions. The futures/options Web API publishes intraday history rather than the one-year stock history. Live candles and Level1 aggregate/index channels require `Normal` mode.

## Documentation

- [StockSharp Fubon Neo connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/fubon_neo.html)
- [Official Fubon Neo user guide](https://www.fbs.com.tw/TradeAPI/en/docs/welcome/)
- [Official SDK download and version notes](https://www.fbs.com.tw/TradeAPI/en/docs/download/download-sdk/)
- [Securities market-data Web API](https://www.fbs.com.tw/TradeAPI/en/docs/market-data/http-api/getting-started/)
- [Securities market-data WebSocket API](https://www.fbs.com.tw/TradeAPI/en/docs/market-data/websocket-api/getting-started/)
- [Futures/options market-data Web API](https://www.fbs.com.tw/TradeAPI/en/docs/market-data-future/http-api/getting-started/)
- [Futures/options market-data WebSocket API](https://www.fbs.com.tw/TradeAPI/en/docs/market-data-future/websocket-api/getting-started/)
- [Securities trading documentation](https://www.fbs.com.tw/TradeAPI/en/docs/trading/introduction/)
- [Futures/options trading documentation](https://www.fbs.com.tw/TradeAPI/en/docs/trading-future/introduction/)
