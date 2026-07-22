# Zhongtai XTP Connector for StockSharp

This directory contains the Zhongtai Securities XTP connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It integrates the official native XTP SDK through a small C ABI bridge.

## Features

- Complete Shanghai, Shenzhen, and Beijing/NQ instrument lookup through `QueryAllTickersFullInfo`.
- Real-time Level 1 and ten-level market depth through the native quote callback stream.
- Tick-by-tick trades for accounts with the corresponding market-data entitlement.
- Cash, IPO, repo, ETF, margin, allotment, and option order instructions.
- Order registration and cancellation, real-time order and execution callbacks, and full daily order/trade lookup.
- Portfolio asset and position snapshots.
- TCP quote/trading sessions and optional UDP quote sessions.

Available instruments, depth, tick-by-tick data, margin operations, and other services depend on the permissions granted to the XTP account by Zhongtai Securities.

## Configuration

- `Login` and `Password` — XTP account credentials.
- `ClientId` — unique client identifier. Regular accounts use a value from 1 through 99.
- `QuoteAddress` and `TransactionAddress` — endpoints supplied with the XTP account.
- `Protocol` — TCP or UDP for the quote session. Trading always uses TCP.
- `LocalAddress` — optional local interface address.
- `SoftwareKey` — software key assigned by Zhongtai Securities.
- `SoftwareVersion` — application version reported to the trader service.
- `DataPath` — writable directory used by the SDK for logs and subscription state.

The package contains the official XTP 2.2.50.8 quote and trader runtimes plus the StockSharp ABI bridge for Windows x64 and Linux x64 (glibc 2.17 or newer). The bridge source and its CMake build are in `NativeBridge`.

## Documentation

- [StockSharp Zhongtai XTP connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/xtp.html)
- [Official XTP portal and documentation](https://xtp.zts.com.cn/)
- [Official XTP API documentation](https://xtp.zts.com.cn/doc/api/xtpDoc)
- [Official XTP Python SDK distribution](https://github.com/ztsec/xtp_api_python)
- [Official XTP Java SDK](https://github.com/ztsec/xtp_api_java)

## Native components

The packaged `xtpquoteapi` and `xtptraderapi` native libraries are from Zhongtai Securities XTP SDK 2.2.50.8 and remain subject to Zhongtai Securities' applicable SDK terms. The StockSharp bridge source is part of this connector.
