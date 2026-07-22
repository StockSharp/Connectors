# StockSharp Fireblocks Connector

The connector integrates StockSharp with a Fireblocks custody workspace. It
uses the official REST API for vault accounts, asset reference data, balances,
transfers, cancellation, and transaction history.

## Access

A Fireblocks API user ID and its PEM-encoded RSA private key are required. The
private key is used locally to sign short-lived RS256 JWTs and is never sent to
Fireblocks. Select the cloud environment that owns the workspace:

- US mainnet/testnet: `https://api.fireblocks.io/v1`;
- EU mainnet/testnet: `https://eu-api.fireblocks.io/v1`;
- EU2 mainnet/testnet: `https://eu2-api.fireblocks.io/v1`;
- US sandbox: `https://sandbox-api.fireblocks.io/v1`.

`ApiEndpoint` may be overridden for an approved Fireblocks gateway or proxy.
It must use HTTPS. The JWT signs the exact request path and query string, and
the SHA-256 hash is calculated from the exact UTF-8 request body.

## Supported operations

- paginated vault-account discovery;
- current and blocked balances for every asset in every loaded vault;
- exact asset lookup by Fireblocks ID or legacy ID;
- paginated workspace asset lookup through the current `/assets` API;
- outgoing `TRANSFER` operations from vault accounts;
- one-time blockchain addresses, including destination tags or memos;
- Fireblocks vault, exchange, connected-account, internal/external wallet,
  contract, network, fiat, and other supported destination objects;
- low, medium, and high network-fee levels;
- gross-amount mode and workspace notes;
- cancellation while the Fireblocks transaction is still cancellable;
- bounded transaction history and continuous transaction-status polling;
- idempotent submission with `externalTxId` and recovery by external ID when a
  response is lost after Fireblocks accepted the transfer.

Each vault is published as `Fireblocks_VAULT_<vault-id>`. To submit a transfer,
use a conditional sell order with `FireblocksOrderCondition.IsWithdraw` set to
`true`. The StockSharp security code is the Fireblocks legacy asset ID. For a
one-time address, set `WithdrawInfo.Type` to `Crypto`, provide
`WithdrawInfo.CryptoAddress`, and use `WithdrawInfo.PaymentId` for a tag or
memo. For a managed Fireblocks destination, select its peer type and set
`DestinationId`.

## Important boundaries

Fireblocks is custody and digital-asset operations infrastructure, not a market
venue. The connector does not fabricate quotes, ticks, candles, an order book,
or trade fills. A StockSharp transaction message represents the lifecycle of a
custody transfer; it is not reported as an exchange trade.

Fireblocks Webhooks V2 are inbound callbacks to a globally reachable HTTPS
endpoint on port 443. A desktop message adapter cannot safely expose or operate
that production endpoint. This connector therefore reconciles vaults and
transactions through the official REST polling flow. Production deployments
that require webhook latency should run a separately secured Webhooks V2
receiver, validate detached JWS signatures against the regional Fireblocks
JWKS, persist events, and feed them into their application infrastructure.

Transaction Authorization Policy, AML/Travel Rule checks, manual approvals,
API Co-Signer behavior, and blockchain confirmation policies remain enforced
by the Fireblocks workspace. A successful API submission may consequently stay
pending, be blocked, be rejected, or fail. The connector publishes those
native lifecycle changes without treating submission as settlement.

## Official documentation

- [Fireblocks developer documentation](https://developers.fireblocks.com/)
- [API authentication and JWT structure](https://developers.fireblocks.com/reference/signing-a-request-jwt-structure)
- [Paginated vault accounts](https://developers.fireblocks.com/api-reference/vaults/get-vault-accounts-paginated)
- [List supported assets](https://developers.fireblocks.com/reference/list-supported-assets)
- [Create a transaction](https://developers.fireblocks.com/api-reference/transactions/create-a-new-transaction)
- [Transaction history](https://developers.fireblocks.com/api-reference/transactions/get-transaction-history)
- [Transaction statuses](https://developers.fireblocks.com/reference/statuses)
- [Monitoring transaction status](https://developers.fireblocks.com/reference/monitoring-transaction-status)
- [Webhooks overview](https://developers.fireblocks.com/reference/webhooks-overview)
- [Validating Webhooks V2](https://developers.fireblocks.com/reference/validating-webhooks)
