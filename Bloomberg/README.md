# Bloomberg Connector for StockSharp

This connector integrates StockSharp with the official Bloomberg BLPAPI .NET SDK and, when enabled, the Bloomberg EMSX API. It does not redistribute Bloomberg libraries, schemas, credentials, or market data.

## Features

- Real-time Bloomberg Level1 quotes and trades through `//blp/mktdata` subscriptions.
- Exact Bloomberg-security lookup through `ReferenceDataRequest`.
- Intraday minute bars through `IntradayBarRequest`.
- Daily, weekly, and monthly history through `HistoricalDataRequest`.
- EMSX order registration and routing, modification, and route cancellation.
- Live EMSX order, route, and fill updates.
- Typed transport models; Bloomberg `Element` objects remain isolated inside the SDK transport layer.

## Requirements

- Bloomberg Terminal API or Server API access with the required market-data entitlements.
- The official Bloomberg BLPAPI .NET SDK and its matching native runtime libraries.
- EMSX enablement, ETORSA acceptance, UUID authorization, and broker approval for order routing.

Bloomberg does not publish the official .NET SDK as a public NuGet dependency. Install it from the Bloomberg API Library, then set `SdkPath` to `Bloomberglp.Blpapi.dll` or its directory. The default Desktop API endpoint is `localhost:8194`.

## Configuration

- `ServerAddress` — BLPAPI host and port.
- `SdkPath` — path to `Bloomberglp.Blpapi.dll` or its containing directory.
- `IsEmsxEnabled` — opens the EMSX service and subscribes to order and route updates.
- `EmsxService` — EMSX service name; production defaults to `//blp/emapisvc`.
- `Broker` — EMSX broker destination used for routed orders.

Security codes must be Bloomberg ticker strings, for example `IBM US Equity`. Security lookup is intentionally exact-symbol only; BLPAPI does not expose an unrestricted entitled universe enumeration. Market depth, historical ticks, cash balances, and positions are not advertised by this connector. Availability of fields and instruments always follows the connected Bloomberg user's entitlements.

## Documentation

- [StockSharp Bloomberg connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/bloomberg.html)
- [Official Bloomberg BLPAPI documentation](https://bloomberg.github.io/blpapi-docs/)
- [Official Bloomberg API Library downloads](https://www.bloomberg.com/professional/support/api-library/)
- [Bloomberg EMSX API documentation](https://emsx-api-doc.readthedocs.io/en/latest/)
- [Bloomberg Server API](https://professional.bloomberg.com/products/data/data-connectivity/server-api/)
