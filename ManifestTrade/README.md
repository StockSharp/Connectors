# Manifest Trade Connector

The connector integrates Manifest Trade's fully on-chain central-limit order
book on Solana through the current Manifest core program, the public market
statistics service used by the official SDK, and standard Solana JSON-RPC and
WebSocket endpoints.

Supported features:

- bounded top-volume mainnet discovery through the Manifest ticker endpoint,
  plus explicitly configured markets;
- strict decoding and validation of the 256-byte market header, dynamic
  red-black-tree order and seat nodes, mints, vaults, and Metaplex metadata;
- full order-book snapshots and Level1 values from actual resting orders;
- realtime book updates through Solana `accountSubscribe` and realtime fills
  through `logsSubscribe`, with JSON-RPC polling as a fallback;
- historical fills from market transaction signatures and the current
  `FillLog` event, plus historical and updating OHLCV candles;
- native SOL, token-wallet, withdrawable seat, and locked-order balances;
- signed market buys and sells through the current core `Swap` instruction;
- signed limit, post-only, and immediate-or-cancel orders through `ClaimSeat`,
  `Deposit`, and `BatchUpdate`;
- atomic cancel-and-replace, individual cancellation, session order tracking,
  confirmations, execution events, network commission, compute budgets, and
  median priority-fee selection.

`MaximumDiscoveredMarkets` controls bounded mainnet discovery. `Markets` adds
semicolon-separated market addresses; optional symbol overrides use
`market|base-symbol|quote-symbol`. Devnet has no mainnet ticker catalogue, so
its markets must be configured explicitly. `MarketDepth` limits the number of
levels emitted on each side.

A wallet is optional for public market data. A public wallet address is needed
for portfolio and open-order data. A base58-encoded 64-byte Solana keypair is
needed only for trading. The public Solana endpoints are usable defaults, but
production systems should configure dedicated HTTP and WebSocket providers
with transaction-history and subscription capacity.

The adapter uses the core program directly rather than the optional wrapper.
Consequently, the on-chain sequence is the exchange order identity and the
core program does not persist arbitrary client-order IDs. Orders submitted by
this adapter can be cancelled and replaced during the session; all current
wallet orders are still reconstructed from market accounts after reconnect.

Token-2022 markets are decoded and published for market data. Direct trading
is currently limited to legacy SPL mints because transfer hooks can require
extension-specific remaining accounts that the Manifest core instruction does
not define. Rejecting those transactions avoids signing an incomplete or
unsafe account list.

Official resources:

- [Manifest Trade](https://www.manifest.trade/)
- [Official Manifest program and SDK repository](https://github.com/Bonasa-Tech/manifest)
- [Current TypeScript SDK](https://github.com/Bonasa-Tech/manifest/tree/main/client/ts)
- [Current slim Rust client](https://github.com/Bonasa-Tech/manifest/tree/main/client/rust/slim)
- [Manifest explorer](https://explorer.manifest.trade/)
- [Solana JSON-RPC methods](https://solana.com/docs/rpc)
- [Solana `accountSubscribe`](https://solana.com/docs/rpc/websocket/accountsubscribe)
- [Solana `logsSubscribe`](https://solana.com/docs/rpc/websocket/logssubscribe)
