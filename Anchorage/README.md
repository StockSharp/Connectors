# StockSharp Anchorage Digital Connector

The connector integrates StockSharp with the Anchorage Digital API for
institutional trading, custody transfers, withdrawals, staking workflows,
balances, and transaction history. It combines the authenticated REST API with
the official trading WebSocket when an Ed25519 signing key is configured.

## Access and configuration

Anchorage API access is commercial and permissioned. Create an API key in a
permission group that grants the required trading, vault, transfer, withdrawal,
or staking permissions. Configure the hexadecimal 32-byte Ed25519 private seed
as `SigningKey` for signed writes and WebSocket access. A read-only API key can
be used without the private seed; in that mode current market snapshots and
private state are reconciled through REST only.

The built-in environments are:

- production REST: `https://api.anchorage.com/v2`;
- production WebSocket: `wss://api.anchorage.com/ws/v2/trading`;
- staging REST: `https://api.anchorage-staging.com/v2`;
- staging WebSocket: `wss://api.anchorage-staging.com/ws/v2/trading`.

REST overrides must use HTTPS and end in `/v2`; WebSocket overrides must use
WSS. Requests always send `Api-Access-Key`. Signed calls additionally send
`Api-Timestamp` and `Api-Signature`. The signature is lowercase-hex Ed25519 over
the Unix-second timestamp, uppercase method, exact path including `/v2` and its
query, and the exact UTF-8 request body.

`MarketDataAccount` optionally scopes customer-specific prices by an exact
trading account ID or name. `MarketDataSubaccount` instead selects an RIA
subaccount. Only one scope can be configured.

## Protocol coverage

- trading-pair and asset discovery with native size and price increments;
- Level 1 and full Level 2 snapshots over WebSocket, with a bounded REST
  polling fallback for read-only keys;
- trading-account, vault, wallet, and asset balance snapshots;
- market, limit, stop-loss, stop-limit, take-profit-limit, RFQ, and all-in
  order routing where enabled for the organization;
- FOK, IOC, and GTC instructions on the documented order endpoints;
- asynchronous-order cancellation, execution-report recovery after reconnect,
  and periodic REST reconciliation;
- order and fill history;
- idempotent internal/external transfers and transfer cancellation;
- quorum-approved withdrawals;
- stake and unstake requests, including provider, validator type, and staking
  position parameters;
- transfer and transaction lifecycle history.

Trading portfolios are named `Anchorage_TRADE_<account-id>`. Custody portfolios
are named `Anchorage_VAULT_<vault-id>`. A custody action is submitted as a
conditional sell order with `AnchorageOrderCondition.Operation` set to
`Transfer`, `Withdrawal`, `Stake`, or `Unstake`. Select a source wallet when a
vault contains more than one eligible wallet. Transfers and withdrawals must
use the ID and type of an Anchorage-approved destination resource; the adapter
does not turn an arbitrary blockchain address into a trusted destination.

## Operational boundaries

Product and endpoint eligibility varies by Anchorage legal entity, account
type, permission group, jurisdiction, and negotiated service. The existence of
an API model does not grant an account access to that workflow. Quorum policies,
mobile approvals, address verification, compliance review, settlement, and
Anchorage risk controls remain authoritative after a request is accepted.

Immediate market/RFQ/all-in calls are terminal FOK workflows. Working limit and
conditional orders use the asynchronous endpoint. Types returned by historical
records but not accepted by that endpoint are preserved for reporting and are
rejected locally for submission.

Anchorage webhooks are inbound HTTPS callbacks. Hosting, authenticating, and
operating a public webhook receiver is outside a desktop message adapter, so
the connector uses the trading WebSocket plus REST reconciliation instead.

Every HTTP and WebSocket payload is represented by a concrete DTO. The
connector does not use dynamic JSON trees, anonymous protocol objects, protocol
dictionaries, or untyped object arrays. All API times are normalized to UTC
`DateTime` values.

## Official resources

- [Anchorage Digital Developer Portal](https://developers.anchorage.com/)
- [Permission groups and API keys](https://developers.anchorage.com/docs/permission-groups-api-keys)
- [Trading API quickstart](https://developers.anchorage.com/docs/trading-api-quickstart)
- [Trading WebSocket overview](https://developers.anchorage.com/docs/websocket-api-getting-started)
- [WebSocket authentication](https://developers.anchorage.com/docs/websocket-api-authentication)
- [WebSocket market data](https://developers.anchorage.com/docs/websocket-api-market-data)
- [Transfers API](https://developers.anchorage.com/docs/transfers-api)
- [Withdrawals API](https://developers.anchorage.com/docs/withdrawal-api)
- [Ethereum staking API](https://developers.anchorage.com/docs/ethereum-staking-api)
