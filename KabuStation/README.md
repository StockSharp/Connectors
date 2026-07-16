# StockSharp Mitsubishi UFJ eSmart kabu Station API connector

This connector integrates StockSharp with the official [kabu Station API](https://kabucom.github.io/kabusapi/ptal/). It talks directly to the REST and WebSocket gateway hosted by the Windows kabu Station application. No browser automation, Python process or unofficial cloud endpoint is used.

## Supported functionality

- Exact-symbol security lookup and reference data for Japanese cash stocks, indices, Osaka futures and Osaka options.
- Realtime Level1, ten-level order books and price ticks through the official PUSH WebSocket.
- Cash, margin, futures and option order entry plus cancellation.
- Native stop orders, Osaka FAS/FAK/FOK, account types, margin modes and explicit position closing.
- Order and execution lookup, cash and derivative wallets, holdings and open positions.
- Automatic token renewal after a `401`, WebSocket reconnection and restoration of registrations owned by the adapter.
- Production and validation environments through the official ports `18080` and `18081`.

All REST requests, REST responses and PUSH messages use typed DTOs. The protocol implementation does not use `JObject`, `JArray`, `JToken`, `dynamic` or dictionaries in place of wire models.

## Configuration

1. Open an account with Mitsubishi UFJ eSmart Securities and enable kabu Station API in the member site.
2. Install and run kabu Station on the same Windows PC as StockSharp.
3. Enable API access in kabu Station and configure its API password.
4. Set `ApiPassword` in the connector. Keep `IsDemo` enabled for the validation gateway; disable it for live trading.
5. Choose the account type and the default Tokyo stock order route. `Sor` is the safe current default for new Tokyo-listed stock orders.

The validation environment returns fixed test values and does not place real orders. The production gateway requires an eligible kabu Station Professional or Premium plan under the broker's current access rules.

## Protocol details and limitations

- Production REST: `http://localhost:18080/kabusapi`; production PUSH: `ws://localhost:18080/kabusapi/websocket`.
- Validation REST and PUSH use port `18081`.
- The gateway accepts requests only from the same PC/IP as the running kabu Station application. kabu Station is normally available from 06:30 until its forced early-morning logout.
- The API has no historical candle endpoint, so this connector does not advertise candle history.
- PUSH covers registered market-data instruments only. The API has no private order or position stream; the adapter polls the official order endpoint every three seconds while subscribed and refreshes portfolios every thirty seconds.
- The global kabu Station registration list is shared by REST lookups and PUSH and is limited to 50 instruments. Information requests can themselves add an instrument to that list. The adapter explicitly unregisters only instruments that it registered for active StockSharp subscriptions.
- PUSH updates are throttled by kabu Station to approximately 400 ms.
- Order endpoints are limited to approximately 5 requests/second; information, wallet and registration endpoints to approximately 10 requests/second. The connector paces both groups below those limits.
- The current API supports order placement and cancellation but not order modification. Replace messages are therefore not advertised.
- Since February 28, 2026, ordinary new cash and margin orders for Tokyo-listed stocks should use SOR (`9`) or Tokyo Plus (`27`) under the broker's best-execution changes. Direct Tokyo (`1`) remains available only in the cases documented by the broker. Market-data lookup still uses Tokyo (`1`).
- Order submission is not automatically repeated after an ambiguous network failure because doing so could create a duplicate order.
- The official Board schema historically names the top sell quote `Bid` and the top buy quote `Ask`. The connector follows the broker's documented Japanese meaning and maps them to StockSharp ask and bid fields respectively.

## Official documentation

- [kabu Station API portal](https://kabucom.github.io/kabusapi/ptal/)
- [REST API reference 1.5](https://kabucom.github.io/kabusapi/reference/index.html)
- [Official OpenAPI specification](https://github.com/kabucom/kabusapi/blob/master/reference/kabu_STATION_API.yaml)
- [PUSH WebSocket reference](https://kabucom.github.io/kabusapi/ptal/push.html)
- [Access setup](https://kabucom.github.io/kabusapi/ptal/howto.html)
- [Limits and operational FAQ](https://kabucom.github.io/kabusapi/ptal/faq.html)
- [Official sample repository](https://github.com/kabucom/kabusapi)

Mitsubishi UFJ eSmart Securities, kabu Station and their marks are the property of their respective owners. StockSharp is not affiliated with or endorsed by the broker.
