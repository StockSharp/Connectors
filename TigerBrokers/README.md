# StockSharp Tiger Brokers OpenAPI connector

This connector integrates StockSharp with Tiger Brokers OpenAPI through the official C# SDK. Signed account and quote requests use the OpenAPI request clients, while realtime market data, order events, executions, positions, and assets use the SDK's persistent TLS/TCP Protobuf push channel.

## Configuration

- `TigerId` is the developer identifier shown on the Tiger OpenAPI developer page.
- `Account` is the live or paper account identifier used by default for trading and portfolio requests.
- `License` selects the Tiger entity that issued the credentials: New Zealand, Singapore, Australia, or Hong Kong.
- `PrivateKey` is the PKCS#8 RSA private key issued for the OpenAPI application.
- `Token` is required by entities that use token authentication, including TBHK. Leave it empty only when the selected entity does not require it.
- `AutoGrabPermission` asks the SDK to acquire the account's available quote permission when the push connection opens.

Treat the private key and token as credentials. Do not write them to logs or source control. Token validity and renewal rules depend on the selected Tiger entity; the connector does not persist or automatically replace credentials.

## Supported operations

- stock lookup for the US, Hong Kong, Singapore, Australia, and mainland China markets;
- futures exchange, contract, and direct contract lookup;
- option expiration and chain lookup when `UnderlyingSecurityId` is supplied;
- realtime Level1 for stocks, options, and futures;
- realtime stock depth and best bid/offer depth for options and futures;
- realtime stock ticks and one-minute stock candles;
- historical stock, futures, and option candles for the intervals supported by each native endpoint;
- market, limit, stop, stop-limit, and trailing orders, including fractional quantities supported by the account;
- order replacement and cancellation;
- REST recovery plus realtime push updates for orders and executions;
- account assets and positions through initial REST snapshots and realtime push updates.

Tiger OpenAPI does not expose the same feeds or candle intervals for every asset class. In particular, tick push and live candle push are stock-only, live candles are one minute, and option discovery requires an underlying symbol. Historical candle requests are capped at 300 records per connector request. Quote depth, exchanges, sessions, order types, and fractional trading remain subject to the selected market and account permissions.

## Push connection and limits

The official SDK owns a single persistent push client per process. The connector therefore rejects a second simultaneous Tiger push connection in the same process. The SDK handles transport reconnects; after each connection acknowledgement the connector restores every active market-data subscription and the account subjects for orders, executions, positions, and assets.

The connector uses typed SDK models and Protobuf callback objects throughout. It does not parse protocol messages through `dynamic`, untyped JSON objects, or dictionaries.

Tiger controls realtime quote permissions and subscription capacity per account, market-data package, and broker entity. `AutoGrabPermission` can acquire an available permission, but it cannot create an entitlement. REST and trading rate limits are enforced by Tiger and may vary by endpoint or account, so applications must honor current API errors and broker policy.

## Official references

- [Tiger OpenAPI C# introduction](https://quant.itigerup.com/openapi/en/csharp/overview/introduction.html)
- [Tiger OpenAPI C# subscriptions](https://quant.itigerup.com/openapi/en/csharp/operation/subscribe/subscribeList.html)
- [Official Tiger OpenAPI C# SDK](https://github.com/tigerfintech/openapi-cs-sdk)

Verify current Tiger documentation, market-data entitlements, and the regulations applicable to the account before production deployment.
