# Daishin CYBOS Plus Connector for StockSharp

This connector integrates StockSharp with the official Daishin Securities CYBOS Plus / CREON Plus Windows COM API. It creates the registered COM objects directly; vendor binaries and generated interop assemblies are not redistributed by this repository or its NuGet package.

## Features

- Korean stock and ETF discovery through `CpUtil.CpCodeMgr`, plus domestic futures and options through the official code managers.
- Level1, live trades, and order books through native COM callbacks. Stocks support KRX, NXT, and the consolidated KRX/NXT feed; domestic derivatives use their dedicated current-price and depth objects.
- Historical minute, daily, weekly, and monthly candles through `CpSysDib.StockChart` and `CpSysDib.FutOptChart`.
- Stock/ETF and domestic futures/options limit or market orders, IOC/FOK/queue instructions, replacement, and cancellation.
- Realtime order and fill notifications through `Dscbo1.CpConclusion` and `Dscbo1.CpFConclusion`.
- Open-order, cash/equity, and position queries for the available stock and derivatives products.
- Native request throttling through `CpUtil.CpCybos.GetLimitRemainCount` and `LimitRequestRemainTime`, plus enforcement of the official 400-subscription realtime ceiling.
- A dedicated STA thread and Windows message pump for COM callbacks. Raw COM values remain inside the bridge; all StockSharp-facing protocol data uses typed models.

## Requirements

- Windows with the current 32-bit CYBOS Plus or CREON Plus package installed.
- The hosting StockSharp process must run as **x86** and with the privileges required by the vendor terminal. A 64-bit process cannot load this in-process COM API.
- A Daishin Securities or CREON account and an active login made through the `CYBOSPLUS` or `CREONPLUS` mode in the vendor terminal. Daishin states that a separate API application is not required.
- Trading must be unlocked in the vendor terminal before `IsTradingEnabled` is enabled.

Keep the official installation under vendor control. Do not copy its COM DLLs or generated interop assemblies into this connector.

## Configuration

- `Account` — optional preferred account number. Leave empty to use the first compatible account for each product.
- `Market` — `Consolidated`, `Krx`, or `Nxt` for stock quotes and stock chart requests.
- `IsTradingEnabled` — initializes `CpTrade.CpTdUtil`, accounts, order routing, open-order/portfolio queries, and realtime order callbacks. Disable it for market-data-only use.

Use `DaishinOrderCondition.Market` to override the stock order route with KRX or NXT. With `Adapter`, the adapter's NXT setting routes to NXT; KRX and consolidated quote settings route orders to KRX.

## Scope and limitations

This implementation covers regular domestic KRX/NXT stocks and ETFs and daytime domestic futures/options. Daishin's separate Eurex/night-session objects, ELW-specific metadata, credit/margin stock orders, conditional/strategy orders, and overseas products are not presented as supported routes. CYBOS Plus does not expose historical individual executions through the objects used here, so tick subscriptions are realtime-only; historical candles remain available.

Instrument metadata exposed by the COM code managers is limited. Expiry and strike fields are emitted only when the installed API returns them. Order-status lookup reports currently open native orders plus orders observed during the connector session; it is not advertised as full historical order storage.

Realtime callbacks are vendor-session callbacks rather than WebSockets. If CYBOS Plus disconnects, the adapter reports the connection loss; StockSharp's normal reconnect path must recreate the COM session and subscriptions after the terminal is logged in again.

## Documentation

- [StockSharp Daishin connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/daishin.html)
- [Official CYBOS Plus site and online help](https://money2.daishin.com/e5/mboard/ptype_basic/CREON_Plus_Notice/DW_Basic_List.aspx?boardseq=283&m=9508&p=8822)
- [Official CYBOS Plus examples and tutorials](https://money2.daishin.com/e5/mboard/ptype_basic/plusPDS/DW_Basic_List.aspx?boardseq=299&m=9508&p=8831)
- [Official NXT API object and order changes](https://money2.daishin.com/e5/mboard/ptype_basic/CREON_Plus_Notice/DW_Basic_Read_Page.aspx?boardseq=283&m=9508&p=8822&page=1&searchString=&seq=114&v=8634)
- [Official Daishin CI and symbol](https://company.daishin.com/group/company/prcenter/ci_system.html)
