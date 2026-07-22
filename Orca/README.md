# Orca Whirlpools Connector

The connector integrates Orca concentrated-liquidity pools on Solana through
the official Whirlpools on-chain program, the official Orca public API, and
standard Solana JSON-RPC and WebSocket endpoints.

Supported features:

- bounded top-volume pool discovery through the official Orca API, plus
  explicitly configured pools;
- on-chain validation of pool, mint, vault, and tick-array state;
- fixed and dynamic tick-array decoding;
- executable Level1 bid and ask probes calculated with the current Whirlpool
  concentrated-liquidity math across five tick arrays;
- historical swaps from pool transaction signatures and official `Traded`
  events;
- realtime swaps through Solana `logsSubscribe`, with JSON-RPC polling as a
  fallback;
- historical and updating OHLCV candles aggregated from actual swap events;
- native SOL and discovered-token wallet balances;
- direct exact-input sells and exact-output buys through the current
  `swap_v2` instruction, including idempotent associated-token-account
  creation, WSOL funding, compute budgets, priority fees, signing, broadcast,
  confirmation tracking, commission, and executed-amount decoding.

`MaximumDiscoveredPools` controls bounded mainnet discovery. `Pools` adds
semicolon-separated pool addresses; optional symbol overrides use
`pool|base-symbol|quote-symbol`. Devnet has no Orca public-API catalogue, so
its pool addresses must be configured explicitly.

A wallet is optional for public market data. A public wallet address is needed
for portfolio data, and a base58-encoded 64-byte Solana keypair is needed only
for trading. Production systems should configure dedicated HTTP and WebSocket
RPC endpoints with sufficient history and subscription capacity.

Adaptive-fee pools are discovered only when explicitly configured, but local
executable quotes and direct swaps are rejected because they require
time-sensitive oracle fee state. Token-2022 pools remain available for public
market data; direct swaps are rejected when transfer-fee or transfer-hook
extensions are active. This matches the current official high-level SDK's
transfer-hook boundary and prevents transactions with incomplete remaining
accounts. Orca is an AMM and has no discrete exchange order book, so the
connector publishes executable quote probes instead of synthetic depth.

Official resources:

- [Orca developer documentation](https://dev.orca.so/)
- [Orca Whirlpools SDK and on-chain program](https://github.com/orca-so/whirlpools)
- [Orca public API reference](https://api.orca.so/docs)
- [Whirlpools account architecture](https://dev.orca.so/Architecture%20Overview/Account%20Architecture/)
- [Solana JSON-RPC methods](https://solana.com/docs/rpc)
- [Solana `logsSubscribe`](https://solana.com/docs/rpc/websocket/logssubscribe)
