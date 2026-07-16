# StockSharp IG Markets connector

The connector integrates StockSharp with the official [IG REST Trading API](https://labs.ig.com/rest-trading-api-guide.html) and [IG Streaming API](https://labs.ig.com/streaming-api-guide.html).

## Supported functionality

- IG demo and live environments.
- Version 2 session login with the `CST` and `X-SECURITY-TOKEN` credentials required by streaming.
- Optional RSA password encryption using IG's current `/session/encryptionKey` response.
- Instrument search and detailed market metadata by EPIC.
- Historical candles through REST, including paging and IG allowance metadata.
- Real-time top-of-book and the official five-level price ladder through `PRICE:{accountId}:{epic}`.
- Real-time chart ticks and 1-second, 1-minute, 5-minute and 1-hour candles.
- Accounts, balances, margin state and open positions.
- OTC market positions, working limit/stop orders, amendments, cancellations and position closes.
- Typed, paged account activity and fill history.
- Real-time confirmations, open-position updates and working-order updates through the account trade stream.

The streaming implementation uses the official `Lightstreamer.DotNetStandard.Client` package and forces secure WebSocket streaming. It does not emulate streaming with REST polling. All IG request, response and streaming JSON data is converted directly to typed DTOs.

## Configuration

1. Create an API key in **My IG > Settings > API keys**.
2. Set `ApiKey`, `UserName` and `Password` on `IgMessageAdapter`.
3. Select `Demo` or `Live` in `Environment`.
4. Optionally set `AccountId`; otherwise the preferred/current account is used.
5. Enable `EncryptPassword` when the account's region requires encrypted-password login.

The Lightstreamer endpoint is always taken from the authenticated session response. It is not hardcoded. The connector uses one streaming connection and restores subscriptions through the Lightstreamer client reconnect mechanism.

## Market-data details

The current `PRICE` stream is used instead of the deprecated `MARKET` stream. It supplies bid/ask prices, tier sizes, quote identifiers, UTC timestamps and dealing state. IG's public chart stream supplies live ticks and only four live candle scales. REST supports the wider historical set exposed by `IgMessageAdapter`.

Historical tick trades are not available from the public REST API and are therefore not fabricated. A REST-only market-depth snapshot is also not advertised because tier sizes exist only in the streaming price subscription.

## Limits and access

An IG account and application API key are required. Demo access is supported. IG currently permits 40 concurrent Lightstreamer subscriptions by default; the connector reserves two for account and trade state and rejects attempts to exceed the limit. REST and historical-data allowances are enforced by IG. Rate-limit responses are retried using `Retry-After` or bounded exponential backoff.

API access is available under IG's account and market-data terms. Exchange permissions and product availability depend on the account and region.

## Official documentation

- [IG Labs](https://labs.ig.com/)
- [REST Trading API guide](https://labs.ig.com/rest-trading-api-guide.html)
- [REST Trading API reference](https://labs.ig.com/rest-trading-api-reference.html)
- [Streaming API guide](https://labs.ig.com/streaming-api-guide.html)
- [Streaming API reference](https://labs.ig.com/streaming-api-reference.html)
- [Session endpoint](https://labs.ig.com/reference/session.html)
- [Password-encryption key endpoint](https://labs.ig.com/reference/session-encryption-key.html)
- [FAQ and API limits](https://labs.ig.com/faq.html)

IG and the IG logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by IG Group.
