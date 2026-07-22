# StockSharp Questrade API connector

This connector integrates StockSharp with the official Questrade API. OAuth and account, reference-data, historical-data, portfolio, and trading operations use REST. Realtime Level 1 quotes and order/execution notifications use Questrade's separate WebSocket services.

## Configuration

- `RefreshToken` is the rotating OAuth refresh token generated or issued for the application. The connector redeems it and keeps both returned tokens in memory.
- `Token` and `ApiServer` can be supplied together when an already redeemed session must be used. `ApiServer` must be the server returned by Questrade, normally ending in `/v1`.
- `Account` optionally chooses one account number. When empty, the primary account is preferred.

Treat all tokens as credentials. A refresh response can contain a replacement refresh token; persist the current adapter settings after a successful connection if the hosting application is responsible for durable credential storage.

## Supported operations

- symbol search and detailed security lookup;
- realtime Level 1 bid, ask, last trade, daily OHLC, volume, delayed-data, and halt fields;
- historical time-frame candles for every Questrade granularity;
- accounts, balances, and positions;
- market, limit, stop, and stop-limit order placement and replacement;
- order cancellation, order recovery, executions, and realtime order/execution notifications.

Questrade's public L1 stream does not provide Level 2 depth or a tick-by-tick trades channel, so the connector does not advertise either capability. Candles are historical REST data; Questrade does not document a realtime candle stream.

## Access and limits

Personal applications can request read-account and read-market-data scopes. The official `trade` scope and order mutation endpoints are restricted to partner applications. Realtime quote quality depends on the account's market-data package; always inspect the delayed-data flag. Starting API L1 streaming can freeze market data in another simultaneously running Questrade IQ client.

Questrade permits only one connection of a selected streaming type at a time. The connector maintains one aggregate L1 WebSocket and one order-notification WebSocket, reconnects them when transport fails, and performs authenticated REST activity before the documented 30-minute session inactivity boundary. Official limits are 30 account calls per second / 30,000 per hour and 20 market-data calls per second / 15,000 per hour; server rate-limit responses remain authoritative.

## Official references

- [Authorization and OAuth](https://www.questrade.com/api/documentation/authorization)
- [Streaming](https://www.questrade.com/api/documentation/streaming)
- [Rate limiting](https://www.questrade.com/api/documentation/rate-limiting)
- [REST operations](https://www.questrade.com/api/documentation/rest-operations/account-calls/accounts)
- [Order placement](https://www.questrade.com/api/documentation/rest-operations/order-calls/accounts-id-orders)

Verify current Questrade documentation, scopes, entitlements, and regulatory requirements before production deployment.
