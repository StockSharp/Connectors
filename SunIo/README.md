# StockSharp SUN.io Connector

The connector integrates StockSharp with SUN.io liquidity on the TRON
blockchain. It uses the official SUN.io read API for token discovery and
indexed router transactions, the Smart Router calculation service for
executable paths, and a TRON FullNode HTTP endpoint for balances, transaction
construction, local signing, broadcast, and confirmation.

## Access

Public SUN.io read endpoints require no credentials for normal low-frequency
queries and document a 10 requests-per-second unauthenticated limit.
`SunApiKey` raises that provider limit when SUN.io has issued a key.
`TronApiKey` is sent as `TRON-PRO-API-KEY` to TronGrid; operators can instead
configure their own FullNode endpoint.

A public TRON address is sufficient for TRX/TRC-20 balances and wallet swap
history. Trading additionally requires its raw 32-byte secp256k1 private key
in hexadecimal form. The key remains local. The connector verifies the
unsigned FullNode transaction against the requested owner, Smart Router,
calldata, call value, fee limit, validity window, and raw-data hash before it
adds a local signature. Only the signed transaction is broadcast.

## Supported operations

- discovery of liquid, non-blacklisted TRC-20 tokens by SUN.io volume;
- explicit `token-address` and `token-address|security-code` configuration;
- TRX/token securities with executable two-sided Smart Router Level1 quotes;
- bounded tick history from completed end-to-end router swaps;
- time-frame candles aggregated from those real indexed swaps;
- native TRX and configured TRC-20 wallet balances;
- wallet order/trade history and current-session transaction lifecycle;
- locally signed native `TRX -> token` Smart Router market swaps;
- per-order slippage and deadline controls;
- confirmed fee reporting from the TRON transaction receipt.

`Markets` accepts semicolon-separated entries in either form:

- `TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t` to generate a code from token metadata;
- `TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t|TRX-USDT` for an explicit StockSharp code.

When `Markets` is empty, the connector selects up to
`MaximumDiscoveredMarkets` tokens above `MinimumLiquidityUsd`. Markets are
oriented as TRX/base and the TRC-20 token/quote. Consequently, a supported
native-source transaction is a StockSharp sell. Amount conversion always uses
the token precision returned by SUN.io; native TRX uses six decimals. Native
TRX and WTRX cannot be configured as destination tokens because either would
form a native/wrapped self-market rather than a routed token market.

## Execution boundary

The documented Smart Router calculation service may return paths across
SunSwap V1/V2/V3, PSM, and SunCurve. The connector validates the whole path,
selects the largest executable output rather than trusting response order,
derives the contract's grouped `versionLen` and fee arrays, and encodes the
documented `swapExactInput` ABI using concrete types.

SunSwap V4 routes are deliberately excluded from signed execution. V4 paths
carry Hook-specific pool keys, while the documented Smart Router
`swapExactInput` interface has no Hook parameter. Sending those paths through
the older ABI would be unsafe. Selling TRC-20 tokens is also not automated in
this version because it requires a separate approval or Permit2 authorization
lifecycle. Read-only reverse quotes and completed reverse swaps remain
available.

SUN.io is an automated-liquidity DEX, not a central limit order book. It does
not provide a comprehensive exchange WebSocket feed or resting order depth.
Level1 values are fresh executable route probes; the connector does not invent
depth, orders, or synthetic ticks. Broadcast blockchain transactions are
irreversible and cannot be cancelled or replaced.

The current official SUN.io protocol matrix documents spot AMMs, routing,
stablecoin modules, mining, and governance, but no perpetual trading API.
Accordingly, this connector does not advertise or emulate perpetuals.

TRON addresses are verified with Base58Check; Solidity parameters and transaction signatures are produced locally.

## Official documentation

- [SUN.io documentation](https://docs.sun.io/)
- [SUN.io REST API](https://docs.sun.io/api/sun-io-api/)
- [Smart Router calculation service](https://docs.sun.io/protocols/smart-router/reference/calculation-service/)
- [Smart Router contract](https://docs.sun.io/protocols/smart-router/reference/contract/)
- [Smart Router swap function](https://docs.sun.io/protocols/smart-router/reference/swap-functions/)
- [TRON FullNode HTTP API](https://developers.tron.network/reference/full-node-api-overview)
- [TRON transaction signing](https://developers.tron.network/docs/tron-protocol-transaction)
- [Official SUN.io GitHub organization](https://github.com/sun-protocol)
