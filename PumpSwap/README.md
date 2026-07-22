# PumpSwap Connector

The connector integrates PumpSwap, the Pump.fun post-graduation Solana AMM,
through the official on-chain program, standard Solana JSON-RPC, and
`logsSubscribe` WebSocket notifications.

Supported features:

- configured-pool loading and validation against the official PumpSwap
  program;
- SPL Token and Token-2022 mint metadata, Metaplex names, symbols, supplies,
  and pool reserves;
- executable Level1 bid and ask probes calculated with the current PumpSwap
  constant-product and dynamic-fee rules;
- historical swaps from pool transaction signatures and transaction logs;
- realtime swaps through the official Solana `logsSubscribe` WebSocket, with
  a JSON-RPC polling fallback;
- historical and updating OHLCV candles aggregated from actual swap events;
- native SOL and configured-token wallet balances;
- direct buy and sell transactions using the current PumpSwap account schema,
  fee tiers, creator fees, cashback accounts, buyback recipients, Token-2022,
  idempotent associated-token-account creation, and WSOL wrapping;
- transaction signing, broadcast, confirmation tracking, network commission,
  and executed-amount decoding from official PumpSwap buy and sell events.

Set `Pools` to semicolon-separated pool addresses. Optional symbol overrides
use `pool|base-symbol|quote-symbol`. PumpSwap does not provide an official
bounded market-index API, and scanning every account owned by the program is
not appropriate for a public RPC endpoint, so the connector deliberately does
not pretend to discover every pool. The default mainnet address is the example
pool published in the official PumpSwap documentation and should be replaced
with the pools needed by the application. Devnet requires explicit pool
addresses.

A wallet is optional for public market data. A public wallet address is needed
for portfolio data, and a base58-encoded 64-byte Solana keypair is needed only
for trading. The standard public Solana endpoints are defaults; production
systems should configure dedicated HTTP and WebSocket RPC endpoints with the
capacity needed for transaction-history reads.

PumpSwap is an automated market maker and has no discrete exchange order book.
The connector publishes executable quote probes as Level1 data and does not
synthesize depth. It covers migrated Pump.fun assets trading in PumpSwap; the
pre-graduation Pump.fun bonding curve is a different program and is outside
this connector. Transactions submitted outside the current adapter session
remain available as market-data events but are not reconstructed as
StockSharp orders.

Official resources:

- [Pump.fun public documentation](https://github.com/pump-fun/pump-public-docs)
- [PumpSwap integration guide](https://github.com/pump-fun/pump-public-docs/blob/main/docs/PUMP_SWAP_README.md)
- [Official PumpSwap SDK guide](https://github.com/pump-fun/pump-public-docs/blob/main/docs/PUMP_SWAP_SDK_README.md)
- [Creator-fee integration](https://github.com/pump-fun/pump-public-docs/blob/main/docs/PUMP_SWAP_CREATOR_FEE_README.md)
- [Cashback integration](https://github.com/pump-fun/pump-public-docs/blob/main/docs/PUMP_CASHBACK_README.md)
- [Official `@pump-fun/pump-swap-sdk` package](https://www.npmjs.com/package/@pump-fun/pump-swap-sdk)
- [Solana JSON-RPC methods](https://solana.com/docs/rpc)
- [Solana `logsSubscribe`](https://solana.com/docs/rpc/websocket/logssubscribe)
