# StockSharp Paxos Connector

The connector integrates StockSharp with the current Paxos Platform v2 API and
the public itBit-derived market-data WebSockets. Public market discovery,
quotes, order books, trades, and candles work without credentials. OAuth2
credentials enable profile balances, brokerage orders, fills, crypto
withdrawals, profile transfers, and stablecoin conversion records.

## Access and configuration

Paxos brokerage and custody access is commercial and permissioned. Create a
Client ID and Client Secret in the Paxos Dashboard, then grant only the scopes
needed by the adapter. `Scopes` is a space-delimited OAuth scope list. Its
default covers the connector's read and write operations; remove permissions
that the client must not use.

The built-in environments are:

- production REST: `https://api.paxos.com/v2`;
- production OAuth: `https://oauth.paxos.com/oauth2/token`;
- production WebSocket: `wss://ws.paxos.com`;
- sandbox REST: `https://api.sandbox.paxos.com/v2`;
- sandbox OAuth: `https://oauth.sandbox.paxos.com/oauth2/token`;
- sandbox WebSocket: `wss://ws.sandbox.paxos.com`.

OAuth uses the client-credentials grant. Access tokens are cached until shortly
before expiration and refreshed after an authorization failure. Private API
calls fail locally when only one credential, no scope, or no credentials are
configured; the public market-data connection remains usable without them.

## Market data

- market discovery with base/quote assets, tick rate, and native size limits;
- current ticker and Level 1 fields;
- REST order-book snapshot followed by the market-specific
  `/marketdata/{market}` WebSocket snapshot and incremental updates;
- recent public executions followed by
  `/executiondata/{market}` streaming trades;
- historical candles for every increment exposed by Paxos, from one minute to
  four weeks.

The public WebSockets do not require authentication or subscription messages;
the selected market is part of the URL. The connector opens only the sockets
needed by active StockSharp subscriptions and shares them between Level 1,
depth, and tick consumers. Snapshot fragments are accumulated through
`final_snapshot`; a zero-size update removes a book level. The first execution
sent after a WebSocket connection is deduplicated by `match_number` against the
REST bootstrap.

## Brokerage and custody

Paxos profiles are published as `Paxos_<nickname>`. A short profile-ID suffix is
added when nicknames collide. The connector supports:

- market, limit, post-only limit, stop-market, and stop-limit orders;
- GTC, IOC, FOK, and GTT instructions;
- order cancellation, bounded history, fills, and periodic REST
  reconciliation;
- crypto withdrawals to an explicit address and `PaxosCryptoNetworks` value;
- internal transfers between profiles of the same customer;
- Paxos transfers to another customer profile;
- the legacy 1:1 fiat/stablecoin conversion lifecycle and cancellation.

A Paxos market buy accepts quote currency only. Set
`PaxosOrderCondition.QuoteAmount`; `OrderRegisterMessage.Volume` remains the
base amount for market sells and all limit orders. Set `IsPostOnly` for a
post-only limit, or `StopPrice` for a stop order.

Custody actions are conditional sell orders. Select the operation in
`PaxosOrderCondition.Operation`, use the security code as the source asset, and
provide the destination profile, address, network, memo, or conversion asset as
required. Third-party integrations can additionally provide `IdentityId` and
`IdentityAccountId`; Paxos requires those two fields together.

Every create request uses a deterministic `ref_id`. When the transport fails
after submission or Paxos reports an idempotency conflict, the connector
queries that reference before reporting failure. Nonterminal private state is
polled because Paxos webhooks are inbound HTTPS callbacks and require a
separately secured public receiver.

## Operational boundaries

API access, assets, markets, transfer networks, identity requirements, travel
rule data, limits, and order features depend on the customer's Paxos product,
legal entity, jurisdiction, and scopes. Paxos compliance review and custody
controls remain authoritative after a request is accepted.

Smart Order Routing WebSockets are allowlisted, authenticated, bidirectional
sessions with a different lifecycle from the public market feeds. This adapter
uses the generally available public WebSockets and the documented REST
brokerage endpoints; it does not claim SOR entitlement.

The Stablecoin Conversion API is deprecated for new integrations in favor of
Orchestrations, but remains modeled for existing customers whose credentials
carry the conversion scopes. Webhook hosting and the newer orchestration rule
engine are outside this desktop adapter.

Every HTTP and WebSocket payload is represented by a concrete DTO. The
connector does not use dynamic JSON trees, anonymous protocol objects,
protocol dictionaries, or untyped object arrays. All parsed API times are
normalized to UTC `DateTime` values.

## Official documentation

- [Paxos Developer Documentation](https://docs.paxos.com/)
- [Authentication](https://docs.paxos.com/guides/developer/authenticate)
- [API credentials and scopes](https://docs.paxos.com/guides/developer/credentials)
- [WebSocket overview](https://docs.paxos.com/api-reference/websockets/overview)
- [Market-data WebSocket](https://docs.paxos.com/api-reference/websockets/market-data-market)
- [Markets and public data](https://docs.paxos.com/api-reference/endpoints/market-data)
- [Create Order](https://docs.paxos.com/api-reference/endpoints/orders/create-order)
- [Profiles](https://docs.paxos.com/api-reference/endpoints/profiles)
- [Crypto withdrawals](https://docs.paxos.com/api-reference/endpoints/crypto-withdrawals)
- [Internal transfers](https://docs.paxos.com/api-reference/endpoints/internal-transfers)
- [Paxos transfers](https://docs.paxos.com/api-reference/endpoints/paxos-transfers)
- [Stablecoin conversions](https://docs.paxos.com/api-reference/endpoints/stablecoin-conversion/overview)
