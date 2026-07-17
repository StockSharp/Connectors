# Capital Futures API Connector for StockSharp

This connector integrates StockSharp with the official Capital Futures / Capital API Windows COM component (`SKCOM.dll`). The official SDK remains a user-installed runtime dependency and is not redistributed by this repository or its NuGet package.

## Features

- Official production and broker-authorized test login through `SKCenterLib`.
- Exact-symbol lookup for TAIFEX domestic futures and options.
- Official `SKQuoteLib` callback feeds for Level1, live trades, and five-level order books.
- Domestic futures orders through `SendFutureOrderCLR` and options orders through `SendOptionOrder`.
- ROD, IOC, and FOK instructions; limit, market, and market-with-protection prices; open, close, and automatic position effects; day-trade and T-session pre-order flags.
- Price modification, quantity reduction, and cancellation by the official 13-character order sequence.
- Realtime order and fill processing through the official `SKReplyLib.OnNewData` callback.
- Domestic futures equity and open-position queries through `GetFutureRights`, `GetOpenInterest`, and the typed 2.13.58 JSON callback.
- A dedicated STA thread and Windows message pump for COM callbacks. Reflection and raw COM values are isolated inside the SDK bridge; StockSharp-facing protocol data uses typed models.

## Requirements

- Windows 10 or newer and a process architecture matching the registered x86 or x64 Capital API component.
- A Capital Futures account with API, quote, trading, and reply permissions, plus the required online API agreements.
- The official C# package. This connector was implemented against Capital API `2.13.58`.
- The matching `SKCOM.dll`, certificate, quote, order, and Solace dependencies kept together and registered with the vendor-provided `install.bat` using Administrator privileges.
- A valid trading certificate or WebCA setup and any two-factor authentication enrollment required for the account.

Download and extract the official C# package, register either its x64 or x86 component directory, and set `SdkPath` to the extracted package directory or its `Interop.SKCOMLib.dll`. Vendor DLLs must not be copied into this repository.

## Configuration

- `SdkPath` — official extracted C# package directory or `Interop.SKCOMLib.dll` path.
- `Login` — Capital Futures login identifier.
- `Password` — electronic trading password.
- `Account` — optional full domestic futures account (broker ID plus account number). Leave empty to select the first `TF` account returned by `OnAccount`.
- `Environment` — `Production` or broker-authorized `Testing`. The connector intentionally does not enable the separate SGX DMA route.
- `IsTradingEnabled` — initialize certificates, accounts, order routing, reply callbacks, and portfolio queries. Disable it for quote-only sessions.
- `LogPath` — optional directory for official SDK logs.

Use `CapitalFuturesOrderCondition` for position effect, market-with-protection pricing, day-trade marking, and T-session pre-orders. Quantity replacement follows the native API rule: an active quantity can be reduced but not increased.

The connector covers domestic TAIFEX futures/options only. It does not expose Capital Securities stocks, foreign stocks, overseas futures/options, SGX DMA, strategy orders, or proxy routing as domestic orders. Capital API does not provide a general historical service through these callbacks, so this connector advertises live Level1, trades, and depth only. Order-status lookup returns the orders observed by the current connector session; realtime exchange state remains authoritative through `OnNewData`.

## Documentation

- [StockSharp Capital Futures connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/capital_futures.html)
- [Official Capital Futures download page](https://www.capitalfutures.com.tw/zh-tw/downloads)
- [Official Capital API trading and documentation page](https://www.capital.com.tw/web/#/download/ApiTrading/ApiTradinginfo)
- [Official Capital API 2.13.58 C# package](https://www.capital.com.tw/Service2/download/api_zip/CapitalAPI_2.13.58_CExample.zip)
