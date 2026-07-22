# StockSharp Korea Investment & Securities Open API connector

This connector integrates StockSharp with the official [KIS Developers Open API](https://apiportal.koreainvestment.com/apiservice). It uses OAuth REST for reference data, historical data, account queries and trading, plus the broker's dedicated WebSocket service for live market data and private order/execution notices.

## Supported functionality

- Korean KRX, NXT and SOR stocks, ETFs and related cash-market products.
- KRX futures and options, including the supported derivatives night-session order route.
- Overseas stocks on NASDAQ, NYSE, NYSE American, Hong Kong, Shanghai, Shenzhen, Tokyo, Hanoi and Ho Chi Minh exchanges.
- Level1 quotes, last trades and native order books over the official WebSocket service.
- REST candle history and live candle aggregation for the StockSharp time frames advertised by the adapter.
- Cash-stock, domestic-derivative and overseas-stock order placement and cancellation.
- Domestic, overseas and derivatives position snapshots, order history and execution recovery.
- Encrypted private WebSocket notices, automatic heartbeat handling, reconnect and subscription restoration.
- Production and KIS simulation environments.

## Configuration

1. Open a KIS securities account and register an application in the KIS Developers portal.
2. Set `Key` and `Secret` to the credentials issued for that application.
3. Set `AccountNumber` to the first eight digits of the account and `ProductCode` to the final two digits, normally `01` for securities or `03` for domestic derivatives.
4. Set `HtsId` when realtime order and execution notices are required.
5. Set `IsDemo` to use the official simulation REST and WebSocket services. Keep production and simulation credentials and accounts separate.

Use the security board to select a market (`KRX`, `NXT`, `SOR`, `KRX-FUT`, `NASDAQ`, `NYSE`, `AMEX`, `HKEX`, `SSE`, `SZSE`, `TSE`, `HNX` or `HOSE`). `KoreaInvestmentOrderCondition.Market` can explicitly select the native KIS market when a board is not available. The condition also exposes native order division, derivatives time in force and night-session routing.

## Protocol details and limitations

- Production REST uses `https://openapi.koreainvestment.com:9443`; simulation uses `https://openapivts.koreainvestment.com:29443`.
- Production and simulation WebSockets use separate broker endpoints on ports `21000` and `31000`. The connector obtains the required approval key, answers `PINGPONG`, decrypts AES-CBC private notices and restores active subscriptions after reconnect.
- KIS assigns different transaction IDs to products, directions and production/simulation operations. Routing is centralized in the connector and follows the current official sample repository.
- The official WebSocket service limits a connection to 40 registrations. The connector enforces this limit across market-data and private channels.
- Candle availability and lookback differ by market and endpoint. Requests are filtered to the returned broker window; unavailable history is not synthesized.
- Overseas market orders and auction divisions are accepted only where the broker and destination exchange support the selected combination.
- Private realtime notices require a valid HTS ID. When it is not configured, order and execution state is recovered by REST polling.
- KIS throttling and per-API limits vary by account and service. The connector retries safe REST reads and HTTP 429 responses but does not automatically repeat an ambiguous failed order submission.

## Official documentation

- [KIS Developers API service](https://apiportal.koreainvestment.com/apiservice)
- [KIS Developers introduction and account setup](https://apiportal.koreainvestment.com/intro)
- [Official Open Trading API repository](https://github.com/koreainvestment/open-trading-api)
- [Official API examples and environment configuration](https://github.com/koreainvestment/open-trading-api/blob/main/kis_devlp.yaml)

Korea Investment & Securities and its marks are the property of their respective owner. StockSharp is not affiliated with or endorsed by Korea Investment & Securities.
