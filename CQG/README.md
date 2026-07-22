# StockSharp CQG Web API connector

This connector integrates StockSharp with CQG Web API 2.296. Authentication, contract metadata, realtime and historical market data, trading, orders, fills, accounts, positions, and account summaries all use typed Google Protobuf messages over CQG's single secure binary WebSocket connection.

## Configuration

- `Endpoint` defaults to CQG's official simulated environment at `wss://demoapi.cqg.com:443`. Obtain production endpoints from CQG API support.
- `Login`, `Password`, and optional `OneTimePassword` enable CQG credential authentication.
- `Token` enables token authentication and is mutually exclusive with username/password credentials.
- `PrivateLabel` and `ClientId` identify the approved firm and application. The public demo values are `WebAPITest`; production identifiers are assigned by CQG.
- `ClientVersion` is reported during logon.
- `Portfolio` optionally selects an authorized CQG brokerage account by account number, account name, or numeric account ID. It is required for order entry when more than one account is authorized.
- `CollapsingLevel` controls the maximum server-side market-data collapsing the client permits. The default is `None`, requesting an unfiltered stream. CQG may disconnect a consumer that cannot keep up, so production applications must process messages promptly.

Credentials, session tokens, and account identifiers are sensitive and must not be written to logs or source control. Demo and production access are issued by CQG; the connector does not embed credentials or emulate a CQG session.

## Supported operations

- exact CQG-symbol resolution and session-specific contract metadata;
- realtime and snapshot Level1 quotes, trades, settlements, and combined DOM;
- historical time and sales with the current default CQG history window;
- historical and streaming whole-minute, whole-hour, daily, and weekly candles;
- market, limit, stop, and stop-limit orders;
- day, GTC, GTD, GTT, IOC/FAK, and FOK durations plus AON, iceberg, quantity-triggered, trailing, market-if-touched, and post-only instructions;
- order registration, replacement, cancellation, state updates, fills, and transaction recovery through CQG trade subscriptions;
- authorized-account discovery, realtime positions, current balances, purchasing power, margin, and realized/unrealized P&L.

CQG contract IDs are valid only within one logged-on session. The connector retains native symbols, discards every contract ID after a reconnect, resolves each active symbol again, and only then restores market-data and candle subscriptions. Order, position, and account streams are restored through a new typed trade subscription.

## Wire protocol and recovery

CQG sends and accepts top-level `ClientMsg` and `ServerMsg` Protobuf payloads in binary WebSocket frames. The connector responds to CQG's server-driven `Ping` with a typed `Pong`, supports fragmented frames, imposes a 32 MB frame safety limit, reconnects with bounded exponential backoff, performs a fresh logon, and restores active subscriptions. It does not synthesize streaming with HTTP polling.

The package vendors the complete 27-file protocol archive currently published by CQG for the demo API. `WebAPI/webapi_2.proto` declares protocol version 2.296, last changed 26 June 2026; the official downloadable archive used here was generated 13 July 2026. Keeping the full schema set avoids partial or hand-written wire models and permits binary-compatible server additions to remain as Protobuf unknown fields.

## Limits and production approval

CQG documents a default client limit of 1,000 top-level messages per 10 seconds, 1,000 information requests per 100 seconds, 200 simultaneous market-data subscriptions, 10 concurrently processed historical requests, and 50 historical subscriptions. The default time-and-sales history depth is 30 days; exact limits, entitlements, delayed/realtime data, and routing permissions are account-specific.

CQG Web API is a commercial enterprise interface. Production use requires CQG credentials, exchange and routing entitlements, and CQG's formal conformance process. The CQG trademark and any `Powered by CQG` presentation must be approved under CQG's current branding requirements; inclusion of the symbol-only connector icon does not grant trademark rights.

## Official references

- [CQG Web API resources](https://partners.cqg.com/api-resources/web-api)
- [CQG Web API help](https://help.cqg.com/apihelp/Documents/cqgwebapi.htm)
- [Getting started](https://help.cqg.com/apihelp/Documents/gettingstarted.htm)
- [Market-data subscriptions](https://help.cqg.com/apihelp/Documents/marketdatasubscription.htm)
- [Official demo protocol archive](https://partners.cqg.com/webapi/protocol/demo/WebAPI.zip)
- [CQG Web API Python samples](https://github.com/cqg/WebAPIPythonSamples)

Confirm the current protocol archive, commercial agreement, conformance checklist, exchange entitlements, and branding approval with CQG before a production release.
