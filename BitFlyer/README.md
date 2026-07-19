# bitFlyer Lightning Connector

The connector integrates current bitFlyer spot markets and bitFlyer Crypto CFD
through the HTTP API and the JSON-RPC 2.0 realtime WebSocket API.

Supported features:

- security discovery for spot and available CFD products;
- Level1 snapshots and realtime ticker updates;
- full order-book snapshots maintained from snapshot and delta channels;
- recent public executions and realtime trade subscriptions;
- balances, collateral, and Crypto CFD positions;
- market and limit child orders with GTC, IOC, and FOK execution policies;
- simple stop, stop-limit, and trailing-stop parent orders;
- individual, product-wide, and filtered cancellation;
- active and historical child/parent orders and account executions;
- authenticated child-order and parent-order events over WebSocket.

bitFlyer does not expose a native candle endpoint, so this connector does not
advertise candle history or realtime candles. Public market data works without
credentials. Portfolio, transaction, and private realtime operations require an
API key with the corresponding permissions.

REST signatures use HMAC-SHA256 over the exact timestamp, HTTP method, request
path (including query), and serialized request body. WebSocket authentication
uses a fresh timestamp and nonce, and all active subscriptions are restored after
reconnect. Every REST and WebSocket payload is represented by a concrete DTO;
the transport does not use dynamic JSON trees or protocol dictionaries.

Official documentation:

- [HTTP API](https://lightning.bitflyer.com/docs)
- [Realtime API overview](https://bf-lightning-api.readme.io/docs/realtime-api)
- [JSON-RPC 2.0 WebSocket](https://bf-lightning-api.readme.io/docs/endpoint-json-rpc)
- [Realtime authentication](https://bf-lightning-api.readme.io/docs/realtime-api-auth)
