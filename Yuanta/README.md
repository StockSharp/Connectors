# Yuanta SPARK API Connector for StockSharp

This connector integrates StockSharp with the official Yuanta SPARK C# SDK for Taiwan securities and domestic futures/options. The Yuanta SDK remains a user-installed runtime dependency and is not redistributed by this repository or its NuGet package.

## Features

- Production and broker-approved UAT login through the official SPARK SDK.
- Exact-symbol instrument lookup for TWSE, TPEx, Emerging, TAIFEX, and quote markets exposed by the connected account.
- Official callback feeds for Level1, trades, and five-level order books.
- Current-session tick history and historical 1/5/15/30/60-minute, daily, weekly, and monthly candles.
- Realtime candle construction from official trade callbacks.
- Securities and domestic futures/options order registration, price/quantity modification, and cancellation.
- Official realtime order/fill reports plus current order, trade, stock-position, futures-position, bank-balance, and futures-equity queries.
- Automatic reconnect with restoration of active market-data subscriptions.

## Requirements

- A Yuanta Securities or Yuanta Futures account with SPARK API access and the required market-data/trading permissions.
- The official C# SDK. This connector was implemented against `YuantaSparkAPI.dll` version `2.2026.0713.0` from the 2026-07-13 package.
- On Linux and macOS, the PFX certificate path and password required by the official SDK. Windows uses the certificate setup supported by Yuanta's SDK.
- Broker approval for UAT and any fixed-IP/firewall registration required by Yuanta.

Download and extract the official C# package, then set `SdkPath` to either the extracted directory or the full path to `YuantaSparkAPI.dll`. Keep the managed dependencies and matching native runtime files in the extracted SDK layout. Vendor binaries must not be copied into this repository.

## Configuration

- `SdkPath` — official extracted SDK directory or `YuantaSparkAPI.dll` path.
- `Account` — the full securities or futures login account expected by Yuanta.
- `Password` — electronic trading password.
- `CertificatePath` and `CertificatePassword` — PFX credentials used by the SDK on Linux and macOS.
- `Environment` — `Production` or broker-approved `Uat`.
- `LogPath` — optional directory for official SDK logs.
- `ReconnectAttempts` — maximum automatic restoration attempts after a failed heartbeat.

Instrument lookup is intentionally exact-symbol: the official SPARK query accepts requested market/symbol pairs and does not publish a complete instrument master through that call. Quote symbols and order-routing commodity codes can differ; set `YuantaOrderCondition.OrderSymbol` from Yuanta's bundled `FunctionList.xlsx` when required. Futures/options orders also require `SettlementMonth`; options require `OptionType` and `StrikePrice`.

The connector routes domestic stock and futures/options orders only. Overseas markets listed by SPARK are exposed for entitled quote subscriptions, but overseas order APIs are not represented as domestic orders. Current-session tick history is limited to the trading day returned by Yuanta. Product access, rate limits, short selling, margin trading, order types, and realtime fields depend on the connected account and Yuanta permissions.

## Documentation

- [StockSharp Yuanta connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/yuanta.html)
- [Official Yuanta SPARK API portal](https://www.yuanta.com.tw/file-repository/content/API/page/index.html)
- [Official SPARK API documentation](https://www.yuanta.com.tw/file-repository/content/sparkapi_docs/index.html)
- [Official C# SDK download](https://ys.yuanta.com.tw/quartet/api/YuantaSparkAPI_CSharp.zip)
