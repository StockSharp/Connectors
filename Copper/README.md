# StockSharp Copper Connector

The connector integrates StockSharp with Copper custody portfolios and
ClearLoop accounts through the official Copper Platform REST API. It publishes
currency reference data, portfolio and wallet balances, ClearLoop delegated
balances, withdrawals, internal transfers, cancellation, and order history.

## Access

A commercial Copper account, an API key, and its HMAC secret are required.
Select the environment that owns the account:

- production: `https://api.copper.co/platform`;
- demo: `https://api.stage.copper.co/platform`;
- testnet: `https://api.testnet.copper.co/platform`.

`ApiEndpoint` may be overridden for an approved Copper gateway or proxy, but
it must use HTTPS and end in `/platform`. Each request sends `Authorization:
ApiKey <key>`, a Unix-millisecond `X-Timestamp`, and a lowercase HMAC-SHA256
`X-Signature`. The signature covers the timestamp, uppercase HTTP method, exact
path including `/platform` and its query, and the exact minified request body.
The secret is retained as a `SecureString` and is never transmitted.

## Supported operations

- Copper currency discovery, including network and decimal metadata;
- active custody, trading, external, and ClearLoop portfolio discovery;
- total, available, locked, reserved, and staked wallet balances;
- ClearLoop main and sub-account balances, including delegated, reserved, and
  currently available-to-undelegate amounts;
- external blockchain withdrawals to a one-time address;
- withdrawals to approved Copper address-book entries;
- transfers between Copper portfolios;
- ClearLoop delegation and undelegation through the documented portfolio-to-
  portfolio withdrawal order flow;
- custody-order cancellation while Copper still permits it;
- bounded order history and continuous lifecycle reconciliation;
- recovery by `externalOrderId` when a create response is lost after Copper
  accepted the operation.

Standard portfolios are published as `Copper_<portfolio-id>`. ClearLoop client
accounts are additionally published as
`Copper_CL_<portfolio-id>_<client-account-id>`, preserving separate delegated
balances for exchange main accounts and subaccounts.

Submit a transfer as a conditional sell order with
`CopperOrderCondition.IsWithdraw` set to `true`. Select `ExternalAddress`,
`AddressBook`, or `Portfolio` as the destination. For an external address,
provide `WithdrawInfo.CryptoAddress`; `WithdrawInfo.PaymentId` or `Memo` carries
a destination tag. For an address-book entry or portfolio, set
`DestinationId`. A local Copper portfolio name may be used as the destination,
or its native Copper ID may be supplied directly.

## Important boundaries

Copper is custody, collateral, settlement, and off-exchange infrastructure. It
does not expose a public execution-market feed or trading WebSocket through the
Copper Platform API. The connector therefore does not fabricate quotes, ticks,
candles, order books, or exchange fills. A StockSharp transaction represents a
Copper custody order and its approval, signing, blockchain, delegation, or
settlement lifecycle; it is not reported as an exchange trade.

Trading against a venue with funds delegated through ClearLoop still takes
place through that venue's own trading API. Copper REST polling reconciles
wallets, delegated balances, and order state. Copper webhooks are inbound HTTPS
callbacks and require a separately secured public receiver; a desktop adapter
does not open such an endpoint.

Copper policies, user approvals, co-signing, address whitelists, settlement
eligibility, and account permissions remain authoritative. A successfully
submitted order can remain pending or require an action before it reaches a
terminal state.

Every HTTP payload is represented by a concrete DTO. The connector does not use
dynamic JSON trees, anonymous protocol objects, protocol dictionaries, or
untyped object arrays.

## Official documentation

- [Copper Developer Portal](https://developer.copper.co/)
- [API authentication](https://developer.copper.co/api-reference/authentication)
- [Copper Platform OpenAPI schema](https://developer.copper.co/copper-open-api-public.json)
- [Fetch portfolios](https://developer.copper.co/api-reference/portfolios/get-portfolios)
- [Fetch wallets](https://developer.copper.co/api-reference/wallets/get-wallets)
- [Create an order](https://developer.copper.co/api-reference/orders/create-order)
- [Delegate funds through ClearLoop](https://developer.copper.co/guides/clearloop/delegate-funds)
- [Undelegate funds through ClearLoop](https://developer.copper.co/guides/clearloop/undelegate-funds)
- [Fetch ClearLoop balances](https://developer.copper.co/api-reference/clearloop/get-clearloop-balances)
