# Meteora DLMM Connector

The connector integrates Meteora Dynamic Liquidity Market Maker pools on
Solana through the official DLMM Data API, the current on-chain DLMM program,
and standard Solana JSON-RPC and WebSocket endpoints.

Supported features:

- bounded top-volume pool discovery through the official Data API, plus
  explicitly configured pool addresses;
- on-chain validation and decoding of DLMM pair, mint, vault, bin-array, and
  optional bitmap-extension accounts;
- executable Level1 quotes and actual market depth reconstructed from current
  initialized bins, including DLMM base and variable fees;
- historical swaps decoded from the program's typed Event CPI data;
- realtime swaps through Solana `logsSubscribe`, followed by typed
  `getTransaction` event decoding, with polling as a fallback;
- official historical OHLCV on mainnet and on-chain swap aggregation on
  devnet;
- native SOL and discovered SPL-token wallet balances;
- signed exact-input sells and exact-output buys through the current `swap2`
  and `swap_exact_out2` instructions, including idempotent associated-token
  accounts, WSOL handling, compute budgets, priority fees, broadcast,
  confirmation, commission, and executed-amount decoding;
- native single-bin limit-order placement for pools that enable the current
  DLMM limit-order function, cancellation and account closing, atomic
  cancel-and-replace, group cancellation, indexed open/closed order status,
  and fill reporting.

`MaximumDiscoveredPools` controls bounded mainnet discovery. `Pools` adds
semicolon-separated pool addresses; optional symbol overrides use
`pool|base-symbol|quote-symbol`. Devnet has no public Meteora pool catalogue,
so its pool addresses must be configured explicitly. `MaximumBinArraysPerSide`
limits the on-chain depth scan around the active bin.

A wallet is optional for public market data. A public wallet address is needed
for portfolio data, while a base58-encoded 64-byte Solana keypair is needed
only for trading. Production systems should configure dedicated HTTP and
WebSocket RPC endpoints with sufficient transaction history and subscription
capacity.

This connector targets Meteora DLMM only. DAMM v1, DAMM v2, Dynamic Bonding
Curve, liquidity-position management, rewards, and pool creation are separate
protocol surfaces and are not represented as trading orders here. Conditional
orders are not supported. Token-2022 pools remain available for public market
data, but direct trading is rejected for mints with extensions because those
transfers can require protocol-specific remaining accounts.

Every REST, JSON-RPC, and WebSocket payload is represented by a concrete DTO.
The transport does not use dynamic JSON trees, anonymous protocol objects,
protocol dictionaries, or untyped object arrays.

Official resources:

- [Meteora DLMM developer guide](https://docs.meteora.ag/developer-guides/dlmm)
- [DLMM program instructions](https://docs.meteora.ag/developer-guides/dlmm/program/instructions)
- [DLMM program events](https://docs.meteora.ag/developer-guides/dlmm/program/events)
- [DLMM Data API overview](https://docs.meteora.ag/developer-guides/dlmm/api-reference/overview)
- [DLMM pool API](https://docs.meteora.ag/api-reference/dlmm/pools/pools)
- [DLMM limit-order API](https://docs.meteora.ag/api-reference/dlmm/limit-orders/get-open-limit-orders-for-pool)
- [Official Meteora DLMM SDK and program source](https://github.com/MeteoraAg/dlmm-sdk)
- [Solana JSON-RPC](https://solana.com/docs/rpc)
- [Solana `logsSubscribe`](https://solana.com/docs/rpc/websocket/logssubscribe)
