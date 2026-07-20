# StockSharp Drift connector

The connector integrates StockSharp with the live successor of Drift Protocol,
currently operated as Velocity Protocol on Solana. The original Drift Data API
is retained by the provider as a historical endpoint and stopped receiving new
trades after the April 2026 incident. Consequently, the connector defaults to
the current Velocity-compatible APIs and on-chain program instead of silently
serving stale Drift data.

## Supported operations

- security discovery and live Level1 market summaries through the Data API
  REST and WebSocket interfaces;
- recent public trades through REST and the DLOB `trades` WebSocket channel;
- aggregated perpetual DLOB snapshots and live books, including vAMM and
  indicative liquidity;
- 1, 5 and 15 minute, 1 and 4 hour, daily, weekly and monthly candles through
  REST and WebSocket;
- public account balances, collateral, positions and open orders for a Solana
  subaccount;
- perpetual market and limit orders, replace, individual cancellation and bulk
  cancellation through the official transaction builder;
- local signing of the serialized Solana transaction followed by submission
  through the official execution endpoint.

The current program exposes spot markets as collateral and borrow/lend assets.
It no longer exposes a spot DLOB, so spot order-book trading is intentionally
rejected while spot balances remain available in portfolio data.

## Configuration

Public market data requires no credentials. For account data, configure either
`AccountAddress` directly or `WalletAddress`; when a wallet is supplied, the
connector discovers its first subaccount through the authority endpoint.

Trading additionally requires `PrivateKey`, a base58-encoded 64-byte Solana
keypair. The key is used only in memory. Before signing, the connector verifies
that the prepared transaction has one signer, that the signer is the configured
wallet and that the transaction invokes the current Velocity program
`vELoC1audYbSYVRXn1vPaV8Axoa9oU6BYmNGZZBDZ1P`.

The default endpoints are:

- `https://data.velocity.exchange`
- `wss://data.velocity.exchange/ws`
- `https://dlob.velocity.exchange`
- `wss://dlob.velocity.exchange/ws`

They are configurable for self-hosted indexers or future provider migrations.
Private state is polled because the current hosted Data WebSocket does not
accept the legacy `user` channel.

## Documentation

- [Velocity Protocol documentation](https://docs.velocity.exchange/)
- [Data API](https://docs.velocity.exchange/developers/data-api)
- [DLOB REST and WebSocket](https://docs.velocity.exchange/developers/ecosystem-builders/orderbook-and-ws)
- [Current OpenAPI contract](https://data.velocity.exchange/openapi.json)
- [Drift recovery portal](https://apps.driftrecovery.trade/)
