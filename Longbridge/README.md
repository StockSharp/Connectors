# StockSharp Longbridge OpenAPI connector

This connector integrates StockSharp with Longbridge OpenAPI. Trading and account snapshots use the REST API. Realtime quotes, depth, trades, and private order updates use Longbridge's two persistent binary WebSocket channels with Protobuf payloads.

## Configuration

- `Key` is the Longbridge application key. In OAuth mode it is the OAuth client identifier.
- `Token` is the legacy application access token or an OAuth access token.
- `Secret` enables legacy HMAC-SHA256 request signing. Leave it empty when `Token` is an OAuth Bearer token.
- `Portfolio` is the name exposed to StockSharp for the token's trading account.
- `ApiUrl`, `QuoteUrl`, and `TradeUrl` default to the global production endpoints. Accounts hosted on the mainland-China environment can use the corresponding `.longbridge.cn` endpoints.

Credentials may carry Longbridge's `us_` or `ap_` data-center prefix. The connector preserves the prefix and sends the matching `x-dc-region` routing header. Treat every token and application secret as a credential and never write it to logs or source control.

## Supported operations

- exact-symbol security lookup using Longbridge symbols such as `AAPL.US`, `700.HK`, `600519.SH`, and `000001.SZ`;
- realtime and snapshot Level1 data through the quote Protobuf channel;
- realtime and snapshot order books and trades;
- historical candles for every interval exposed by the current quote protocol, from one minute through one year;
- market, limit, enhanced-limit, auction, odd-lot, touched, and trailing orders supported by the account and market;
- order replacement and cancellation;
- today and historical order/execution recovery plus realtime private order and execution updates;
- account balances and stock positions.

Longbridge does not expose a complete security-master search through the quote protocol. Security lookup therefore requires an exact native symbol. Candle subscriptions return historical data and finish; Longbridge does not publish a candle push stream. Instrument types, order types, outside-RTH policies, and quote depth remain subject to the selected market and account permissions.

## Streaming, reconnects, and limits

The connector obtains a short-lived socket OTP through `/v2/socket/token`, authenticates both official WebSocket gateways, sends a protocol heartbeat every 60 seconds, reconnects with exponential backoff, obtains a new OTP after every reconnect, and restores all active quote and private-trade subscriptions.

The quote gateway limits an account to 500 subscribed symbols. Basic quote permissions and the API itself can be free, but realtime Hong Kong, Level2, options, and other exchange data may require a paid entitlement. REST and socket command limits depend on the endpoint and profile; the connector honors HTTP `Retry-After`, spaces trading calls by at least 20 milliseconds, and surfaces remaining server rejections to StockSharp.

## Official references

- [Longbridge OpenAPI getting started](https://open.longbridge.com/docs/getting-started)
- [Quote API overview](https://open.longbridge.com/docs/quote/overview)
- [Quote subscriptions](https://open.longbridge.com/docs/quote/subscribe/overview)
- [Socket binary protocol](https://open.longbridge.com/docs/socket/protocol/overview)
- [Official Protobuf definitions](https://github.com/longbridge/openapi-protobufs)
- [Official protocol implementation](https://github.com/longbridge/openapi-protocol)
- [OpenAPI pricing and quote packages](https://open.longbridge.com/pricing)

Verify current API permissions, market-data entitlements, and regulations applicable to the account before production deployment.
