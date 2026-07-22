# StockSharp Kiwoom REST API connector

This connector integrates StockSharp with the official [Kiwoom REST API](https://openapi.kiwoom.com/). It uses OAuth REST for instruments, historical data, account queries and trading, and the broker's JSON WebSocket services for live market data and private order, execution and balance notifications.

## Supported functionality

- Korean stocks, ETFs and related cash-market products on KRX, NXT and Kiwoom SOR.
- US stocks and ETFs on NASDAQ, NYSE and NYSE American.
- Complete domestic and US instrument lists through the broker's reference endpoints.
- Level1, last trades and native ten-level order books over WebSocket.
- REST minute and daily candle history plus live candle aggregation for the time frames advertised by the adapter.
- Domestic and US order placement, replacement and cancellation, including native auction, IOC/FOK, midpoint, stop, VWAP and TWAP divisions where the selected market supports them.
- Domestic and US positions, open orders and execution recovery through REST.
- Private WebSocket order, execution and domestic balance updates.
- Automatic OAuth renewal, PING handling, reconnect and restoration of all active subscriptions.
- Production and the official domestic mock environment.

Every REST request, response and WebSocket frame used by the connector is represented by a typed DTO. Numeric realtime FIDs are mapped to typed properties; the connector does not use `JObject`, `JArray`, `JToken`, `dynamic` or protocol dictionaries.

## Configuration

1. Open a Kiwoom securities account and register an application in the Kiwoom REST API portal.
2. Set `Key` and `Secret` to the credentials issued for the application.
3. Set `IsDemo` to use `mockapi.kiwoom.com`. The mock environment officially supports domestic KRX functions only.

Use `KRX`, `NXT`, `SOR`, `NASDAQ`, `NYSE` or `AMEX` as the security board. `KiwoomOrderCondition.Market` can explicitly select a native market when a board is unavailable. The condition also exposes the native order division, domestic time in force and stop price.

## Protocol details and limitations

- Production REST uses `https://api.kiwoom.com`; mock REST uses `https://mockapi.kiwoom.com`.
- Domestic realtime uses `wss://api.kiwoom.com:10000/api/dostk/websocket`; US realtime uses `wss://api.kiwoom.com:10000/api/us/websocket`. The corresponding mock host is used for domestic demo sessions.
- WebSocket login uses the OAuth access token. The connector answers broker `PING` frames unchanged and restores market-data and private subscriptions after reconnect.
- Kiwoom allows up to 200 realtime symbols per WebSocket session. The connector enforces that limit independently for domestic and US sessions. Private F4/F5 events are account-scoped and do not consume a ticker registration.
- Production domestic order and query endpoints are generally limited to five requests per second. US orders allow up to ten requests per second and queries up to five; lower peak-session limits can apply. Mock TRs are limited to one request per second. The connector uses a conservative client-wide throttle and honors HTTP retry delays.
- Only one active realtime condition-search session is allowed per registered condition. Condition search is not advertised by this first StockSharp adapter version.
- The mock environment is KRX-only. US functions require production credentials and an enabled account.
- Historical availability and page depth are controlled by Kiwoom. The connector follows continuation headers, filters the returned window and does not synthesize missing bars or trades.
- The US replace endpoint changes price but not quantity. To change quantity, cancel the order and submit a new one.
- The connector retries safe throttled or server-failed REST requests. It does not automatically repeat an order submission whose outcome is ambiguous.

## Official documentation

- [Kiwoom REST API portal](https://openapi.kiwoom.com/)
- [API guide](https://openapi.kiwoom.com/guide/apiguide)
- [Service introduction, limits and realtime constraints](https://openapi.kiwoom.com/intro)
- [Official Kiwoom REST API SDK and examples](https://github.com/Kiwoom-Securities/Kiwoom-REST-API)
- [Domestic and US WebSocket API guide](https://openapi.kiwoom.com/guide/apiguide?jobTpCode=40)

Kiwoom Securities and its marks are the property of their respective owner. StockSharp is not affiliated with or endorsed by Kiwoom Securities.
