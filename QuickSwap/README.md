# StockSharp QuickSwap Connector

The connector integrates QuickSwap liquidity on Polygon PoS through the
official constant-product v2 contracts, Algebra concentrated-liquidity v3
contracts, EVM JSON-RPC, and optional compatible subgraphs.

## Supported operations

- configured and discovered pool verification through the official v2 and
  v3 factories;
- optional discovery of leading pools through QuickSwap-compatible subgraphs;
- on-chain ERC-20 metadata and balances for loaded markets plus the native
  POL wallet balance;
- executable exact-input and exact-output Level1 quote probes;
- historical and polled realtime v2 and v3 swaps from configured subgraphs;
- OHLCV candles aggregated from those real swaps;
- exact-input and exact-output swaps through the official v2 and v3 routers;
- ERC-20 allowance reset and approval transactions;
- local EIP-1559 or legacy signing, broadcast, receipt tracking, gas fees,
  executed-amount decoding, and current-session order status.

An HTTP Polygon JSON-RPC endpoint and a public wallet address are required.
The raw 32-byte EVM private key is needed only for trading and remains local.
The connector validates the configured chain and contract-derived pool before
it exposes a market. It estimates gas before signing and submits only the raw
signed transaction.

`Markets` contains semicolon-separated
`version|base-token|quote-token` entries. For example:

- `V3|0x0d500B1d8E8eF31E21C99d1Db9A6444d3ADf1270|0x2791Bca1f2de4661ED88A30C99A7a9449Aa84174`
- `V2|0x8f3Cf7ad23Cd3CaDbD9735AFf958023239c6A063|0x2791Bca1f2de4661ED88A30C99A7a9449Aa84174`

Algebra v3 uses one pool per token pair and calculates its fee dynamically,
so a v3 fee tier is neither configured nor encoded in the swap call. Native
POL must be represented by WPOL inside an AMM pool; native POL itself is still
reported as a portfolio balance and pays transaction gas.

## Subgraphs and execution boundary

`V2Subgraph` and `V3Subgraph` accept either an absolute HTTPS GraphQL endpoint
or a The Graph deployment ID. A deployment ID also requires `GraphApiKey`.
QuickSwap publishes the schemas and mapping sources, but its documentation
does not guarantee a permanent public endpoint, so the connector does not
embed a stale hosted-service URL. Configured markets, balances, quotes, and
trading work without a subgraph; discovery, ticks, and candles require the
corresponding configured source.

The connector deliberately executes direct two-token pools. It does not
imitate the web application's aggregated multi-hop router. QuickPerps is a
separate product and is not represented as AMM spot orders by this connector.

QuickSwap is an AMM and has no central-limit order book or comprehensive
exchange WebSocket. Level1 values are fresh contract quote simulations. The
connector does not invent depth or synthetic trades. On-chain transactions
are irreversible, and transactions submitted outside the current adapter
session cannot be reconstructed as StockSharp orders from a receipt alone.

## Official documentation

- [QuickSwap documentation](https://docs.quickswap.exchange/)
- [QuickSwap contracts and addresses](https://docs.quickswap.exchange/overview/contracts-and-addresses)
- [QuickSwap v3 factory](https://docs.quickswap.exchange/technical-reference/smart-contracts/v3/factory)
- [QuickSwap subgraph sources](https://github.com/QuickSwap/v3-subgraphs)
- [Algebra v3 swap router](https://docs-v1.algebra.finance/en/docs/contracts/API-reference-v1.0/periphery/SwapRouter/)
- [Algebra single-swap guide](https://docs-v1.algebra.finance/en/docs/contracts/guides/swaps/single-swaps/)
