# StockSharp THORChain Connector

The connector integrates StockSharp with THORChain native Layer-1 liquidity.
It uses Midgard for pools and completed actions, THORNode for executable swap
quotes and protocol state, and Cosmos REST for native RUNE balances and signed
transaction submission.

## Access

Public pool data and quotes do not require an account or API key. The default
Liquify endpoints are public infrastructure. Their documented allowance is
50,000 requests per day per IP, while the THORNode quote endpoint is limited
to one request per second. `ClientId` is sent as `x-client-id` so an operator
can identify legitimate application traffic. A production deployment should
configure its own THORNode and Midgard endpoints or an appropriate provider.

A `thor1...` address is sufficient for RUNE balances and wallet action
history. Trading additionally requires its raw 32-byte secp256k1 private key
in hexadecimal form. The key stays local. The connector constructs and signs
the Cosmos `TxRaw` bytes and sends only the signed transaction to THORNode.

## Supported operations

- discovery of available, liquid Layer-1 pools from Midgard;
- explicit `CHAIN.ASSET` pool and security-code configuration;
- RUNE/destination-asset securities with executable two-sided Level1 quotes;
- bounded public tick history for completed direct RUNE swaps;
- time-frame candles aggregated only from those real direct swaps;
- native RUNE wallet balance and wallet-specific swap history;
- streaming `RUNE -> L1 asset` market swaps with a destination-chain address;
- per-order liquidity tolerance, streaming interval, streaming quantity, and
  optional refund address;
- local secp256k1 `MsgDeposit` signing, Cosmos synchronous broadcast, and
  Midgard lifecycle tracking through pending, success, or refund.

`Markets` accepts semicolon-separated entries in either form:

- `BTC.BTC` to use an automatically generated security code;
- `BTC.BTC|RUNE-BTC` to provide an explicit StockSharp code.

When `Markets` is empty, the connector selects up to
`MaximumDiscoveredMarkets` pools above `MinimumLiquidityUsd`. THORChain wire
amounts use the protocol's 1e8 accounting precision. A StockSharp market is
oriented as RUNE/base and destination asset/quote, so a supported signed swap
is registered as a market sell. `THORChainOrderCondition.DestinationAddress`
must be valid for the selected destination chain; THORNode validates it while
building the quote and memo.

## Important boundaries

THORChain is a cross-chain automated liquidity protocol, not a central limit
order book. It does not publish resting depth or a comprehensive exchange
WebSocket feed. Level1 values are fresh executable quote probes, and the
connector does not invent market depth, public orders, or synthetic ticks.

The public RUNE/asset tape includes only successful actions whose actual input
or output contains native `THOR.RUNE`. Asset-to-asset swaps route through the
protocol but Midgard does not expose a separately executed RUNE transfer for
that internal leg, so those actions are not misreported as RUNE-pair trades.
History is bounded by `HistoryMaximum` and Midgard's 50-action pages.

This connector deliberately signs only native THORChain transactions funded
with RUNE. Starting a swap from BTC, ETH, SOL, XRP, or another external chain
requires that chain's wallet, UTXO/account selection, fee logic, signing, and
RPC provider. Those unrelated private-key domains are not emulated. Such a
swap can still be quoted and observed, but it is not submitted by this
connector. Broadcast swaps are irreversible and cannot be cancelled or
replaced.

Every JSON request and response uses concrete DTOs. Cosmos and THORChain
messages are encoded from their concrete protobuf fields; no dynamic JSON
trees, anonymous protocol objects, or protocol dictionaries are used.

## Official documentation

- [THORChain developer documentation](https://dev.thorchain.org/)
- [Connecting to THORChain](https://dev.thorchain.org/concepts/connecting-to-thorchain.html)
- [Swap quickstart and quote contract](https://dev.thorchain.org/swap-guide/quickstart-guide.html)
- [Sending native transactions](https://dev.thorchain.org/concepts/sending-transactions.html)
- [Asset notation](https://dev.thorchain.org/concepts/asset-notation.html)
- [Official THORNode source](https://gitlab.com/thorchain/thornode)
- [Official Midgard source](https://gitlab.com/thorchain/midgard)
