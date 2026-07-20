# StockSharp Raydium Connector

The connector integrates StockSharp with Raydium liquidity on Solana. It uses
the official Raydium API v3 for pool discovery, the official Trade API for
routed quotes and swap transaction construction, Solana JSON-RPC for account
state and history, and Solana WebSocket `logsSubscribe` for live pool activity.

## Access

Raydium's public read and Trade API endpoints do not require an API key. A
Solana wallet address is required for portfolio data. Trading additionally
requires the wallet's base58-encoded 64-byte keypair. The private key is used
locally to sign the serialized transaction returned by the Trade API and is
never sent to Raydium.

Public Solana RPC endpoints impose restrictive request and WebSocket limits.
Use a dedicated Solana RPC provider for production workloads and configure its
HTTP and WebSocket endpoints in `RpcEndpoint` and `StreamingEndpoint`.

## Supported operations

- top-volume pool discovery and explicit pool configuration;
- pair securities grouped across Raydium CPMM, CLMM, and AMM v4 pools;
- routed executable Level1 quotes through the official Trade API;
- synthetic market depth built from successive cumulative routed quotes;
- live and historical direct-pool swaps decoded from on-chain vault balance
  changes;
- time-frame candles aggregated from those decoded swaps;
- native SOL and discovered SPL/Token-2022 wallet balances;
- exact-input sells and exact-output buys as immediate market swaps;
- V0 transaction signing, ordered setup-transaction confirmation, receipt
  tracking, orders, and fills.

`Pools` accepts semicolon-separated entries in either `pool` or
`pool|base-symbol|quote-symbol` form. Symbol overrides are useful for tokens
whose on-chain/API symbol is missing or unsuitable as a StockSharp code.

## Important boundaries

Raydium is an AMM and does not expose a central limit-order book. Market depth
is therefore an executable quote ladder, not resting orders. Swaps are
immediate market operations and cannot be cancelled, replaced, made post-only,
or assigned a time in force.

The Trade API may route a submitted swap through any supported Raydium path.
Live trades and candles, however, contain swaps that directly touch a
configured or discovered pool for the selected pair. They are not a synthetic
market-wide tape reconstructed from every possible multi-hop route.

The connector does not currently expose liquidity provision, farming,
LaunchLab, referral fees, or Raydium Perps. A Trade API quote is short-lived;
the connector obtains it immediately before transaction construction. Token
trading and portfolio balances use the wallet's canonical associated token
accounts. Order-status history covers swaps submitted during the current
adapter session because Raydium swaps do not create persistent exchange orders.

## Official documentation

- [Raydium developer documentation](https://docs.raydium.io/)
- [SDK and API overview](https://docs.raydium.io/sdk-api)
- [REST API surface](https://docs.raydium.io/sdk-api/rest-api)
- [Trade API](https://docs.raydium.io/sdk-api/trade-api)
- [Program addresses](https://docs.raydium.io/reference/program-addresses)
- [Official Raydium GitHub organization](https://github.com/raydium-io)
