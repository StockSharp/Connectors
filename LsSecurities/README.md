# StockSharp LS Securities Open API connector

This connector integrates StockSharp with the official [LS Securities Open API](https://openapi.ls-sec.co.kr/). It uses OAuth REST for reference data, history, account queries and cash-equity trading, plus the broker's JSON WebSocket service for live market data and private order events.

## Supported functionality

- Korean KOSPI and KOSDAQ cash equities, ETFs, ETNs and ELWs returned by the `t8436` instrument master.
- Realtime unified-market trades and Level1 through `US3`.
- Native ten-level unified KRX/NXT order books through `UH1`.
- Intraday N-minute and daily, weekly and monthly historical candles through `t8412` and `t8410`.
- Current-session tick history through `t1301`.
- Cash-equity order registration, replacement and cancellation through `CSPAT00601`, `CSPAT00701` and `CSPAT00801`.
- Current orders and executions through `t0425`, positions and account valuation through `t0424`.
- Private order receipt, fill, replacement, cancellation and rejection events through `SC0`–`SC4`.
- WebSocket reconnect with restoration of market-data and private subscriptions.

## Configuration

1. Open an LS Securities account and register for both XingAPI and Open API on the LS Securities website.
2. Create separate production or simulation application credentials as required by LS Securities.
3. Set `Key` and `Secret` to the issued credentials.
4. Set `IsDemo` for the simulation WebSocket endpoint. LS routes REST requests to production or simulation according to the credentials.
5. Optionally set `Account` to the portfolio label that StockSharp should expose. Application credentials are issued per account, so the account number is not included in order payloads.

`LsSecuritiesOrderCondition` exposes the native price type, KRX/NXT routing request, margin transaction code and loan date. A margin code of `000` is a cash order. StockSharp DAY, IOC and FOK values map to LS condition codes `0`, `1` and `2`.

## Protocol details and limitations

- REST uses `https://openapi.ls-sec.co.kr:8080`. Production WebSocket uses `wss://openapi.ls-sec.co.kr:9443/websocket`; simulation uses port `29443`.
- Access tokens are issued with the client-credentials grant and normally remain valid until 07:00 Korea time on the following day. REST authentication is renewed before expiry, and WebSocket restoration obtains the current token after a server reconnect.
- The connector uses current unified `US3` and `UH1` feeds so a symbol can reflect KRX and NXT activity without maintaining separate legacy KOSPI/KOSDAQ channels.
- Broker TR limits differ by operation. The client serializes REST calls and applies the published per-TR ceilings; it retries only safe queries after transient failures. It never automatically replays an order request with an ambiguous outcome.
- Historical tick TR `t1301` is a current-session service. The broker controls available history depth and continuation windows.
- Native WebSocket candle packets are not offered. Candle subscriptions return official REST history and finish; the connector does not fabricate live candles.
- This first version deliberately advertises domestic cash equities only. LS also documents domestic derivatives, overseas futures and overseas equities, but those products use different TR schemas and are not declared supported here.
- Conditional searches and specialist investment-information feeds are not exposed as StockSharp market-data types.

## Official documentation

- [LS Securities Open API portal and API guide](https://openapi.ls-sec.co.kr/)
- [Account registration, credentials and access-token lifecycle](https://openapi.ls-sec.co.kr/howto-use)
- [Official REST and WebSocket examples](https://openapi.ls-sec.co.kr/howto-sample)
- [API service catalogue](https://openapi.ls-sec.co.kr/apiservice)

LS Securities and its marks are the property of their respective owner. StockSharp is not affiliated with or endorsed by LS Securities.
