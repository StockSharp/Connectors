# StockSharp CTP connector

The connector integrates StockSharp with the native Shanghai Futures Information
Technology CTP Trader and Market Data APIs used by mainland Chinese futures
brokers. It supports SHFE, DCE, CZCE, CFFEX, INE, and GFEX contracts made
available by the selected broker.

## Supported operations

- Native asynchronous Trader and Market Data fronts with independent connection state.
- Client authentication, user login, and mandatory settlement confirmation.
- Futures and options instrument lookup through the Trader API.
- Real-time Level 1, five-level market depth, and trade ticks derived from CTP cumulative-volume updates.
- Limit, market, IOC, FAK, FOK, stop, open, close, close-today, and close-yesterday instructions.
- Order registration and cancellation, live order and fill notifications, and order/trade recovery queries.
- Investor positions and trading-account balances, margin, commission, and PnL.
- Topic recovery using restart, resume, or quick modes.

The standard CTP API does not provide historical candles or historical market
data. The connector deliberately does not advertise either capability.

## Configuration

Production front addresses, `BrokerId`, credentials, `AppId`, authentication
code, and the exact supported SDK version must come from the futures broker.
`InvestorId` can be left empty when it is identical to the login user ID.

For development, [SimNow](https://www.simnow.com.cn/product.action) publishes
test fronts and credentials. At the time of writing its 7x24 environment uses:

- Broker ID: `9999`
- Trader front: `tcp://180.168.146.187:10130`
- Market Data front: `tcp://180.168.146.187:10131`
- App ID: `simnow_client_test`
- Authentication code: `0000000000000000`

SimNow environments, front addresses, trading hours, account eligibility, and
credentials can change. Always verify the current values on the official product
page. The normal trading-session environments should be preferred for exchange-
accurate testing.

CTP brokers usually throttle synchronous query requests. `QueryInterval`
defaults to one second and serializes instrument, order, trade, position, and
account queries. Order submission and cancellation are not delayed by this
setting.

## Native ABI

The packaged bridge and native runtimes use CTP 6.7.11. CTP versions are not a
single stable ABI: a production broker can require a different or customized
build. In that case replace the native CTP libraries with the broker-approved SDK
and rebuild `NativeBridge` against that SDK. Do not mix headers and runtime
libraries from different versions.

The package contains Windows x64 and Linux x64 runtime layouts. To rebuild the
bridge, set `CTP_SDK_ROOT` to a CTP SDK directory containing the four API headers
and `win_x64` or `linux_x64`, then run CMake. The bridge exports a small typed C
ABI, converts GB18030 text to UTF-8, and keeps CTP C++ structures out of managed
code.

## Operational and regulatory notes

Market-data permissions, exchanges, products, order instructions, production
mode, and terminal-information reporting are broker-specific. A native return
code only confirms that the local API accepted a request; final acceptance or
rejection arrives asynchronously.

China's futures program-trading reporting regime took effect on October 9, 2025.
Confirm reporting, approval, static-IP, terminal collection, and risk-control
requirements with the broker before production use.

## Documentation

- [Official SimNow CTP SDK downloads](https://www.simnow.com.cn/static/apiDownload.action)
- [Official SimNow environments](https://www.simnow.com.cn/product.action)
- [Shanghai Futures Information Technology](https://www.shfe.com.cn/sfit/)
- [Example broker external-access requirements](https://www.citicf.com/e-futures/csc/app/external_access)
- [StockSharp connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/ctp.html)

See [third-party notices](THIRD_PARTY_NOTICES.md) for native-runtime and bridge
attribution.
