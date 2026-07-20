# StockSharp Jupiter Connector

The connector integrates StockSharp with Jupiter on Solana. Spot routing uses
the official Swap API v2 for executable exact-input and exact-output quotes,
transaction construction, managed submission, and confirmation. Perpetual
trading uses the official Jupiter Perps API contract published through the
Jupiter CLI for SOL, BTC, and ETH markets.

## Access

Public quotes and token metadata work without credentials through
`https://api.jup.ag`. Jupiter currently limits keyless access to roughly 0.5
requests per second. An optional Jupiter API key raises the default free tier
to roughly one request per second; production rate limits depend on the
selected Jupiter plan.

A Solana wallet address is required for balances, positions, active Perps
requests, and private trade history. Trading additionally requires the
wallet's base58-encoded 64-byte keypair. The key remains local: the connector
only signs the serialized transaction returned by Jupiter and sends the signed
transaction to the corresponding execution endpoint.

## Supported operations

- configurable spot pairs and verified metadata through Tokens API v2;
- SOL-USDC, JUP-USDC, BTC-USDC, and ETH-USDC spot pairs by default;
- SOL-PERP, BTC-PERP, and ETH-PERP perpetual securities;
- executable two-sided spot Level1 quotes using Swap API v2 `ExactIn` and
  `ExactOut` routing;
- Jupiter Perps price, 24-hour high, low, change, and volume Level1 data;
- native SOL and SPL/Token-2022 portfolio balances;
- Jupiter Perps positions with side, entry, mark, PnL, leverage, and
  liquidation price;
- spot market swaps with exact base-token volume on both sides;
- Perps market opens, limit opens, full or partial position reductions,
  take-profit, and stop-loss requests;
- Perps limit and TP/SL replacement and cancellation;
- session transactions, active requests, and wallet spot/Perps trade history.

`SpotMarkets` accepts semicolon-separated entries in
`base-mint|quote-mint|security-code` format. The security code is optional and
is derived from token symbols when omitted. Explicit mint addresses prevent an
unrelated token with the same symbol from being selected.

For a Perps order, `JupiterOrderCondition` controls the operation, collateral,
settlement token, target leverage, position public key, and optional attached
TP/SL prices. StockSharp volume is expressed in units of the underlying asset;
the connector converts it to the USD size and collateral amount required by
Jupiter.

## Important boundaries

Jupiter is a route aggregator, not a central limit-order book. It does not
publish a comprehensive aggregate WebSocket trade tape or resting depth feed.
Spot Level1 is therefore polled executable liquidity, and the connector does
not invent public ticks, candles, or order-book rows. Swap API v2 may select
Metis, JupiterZ, DFlow, OKX, or another supported router for each request.

Spot swaps are atomic market transactions and cannot be cancelled or replaced.
Perps limit and TP/SL requests are persistent on-chain requests and do support
those operations. Order history is wallet-specific; Jupiter does not expose a
public market-wide execution history for every aggregate route.

The Perps API remains a developing interface. This connector follows the
typed `/v2` contract used by the official Jupiter CLI and keeps its endpoint
configurable so deployments can follow compatible endpoint migrations.

## Official documentation

- [Jupiter developer documentation](https://developers.jup.ag/)
- [Jupiter Swap API v2](https://developers.jup.ag/docs/swap)
- [Order and managed execution](https://developers.jup.ag/docs/swap/order-and-execute)
- [Jupiter Perps developer documentation](https://developers.jup.ag/docs/perps)
- [Official Jupiter CLI](https://github.com/jup-ag/cli)
