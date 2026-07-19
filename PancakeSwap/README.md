# PancakeSwap Connector

The connector integrates PancakeSwap v2 and v3 AMM pools through official
EVM contracts, JSON-RPC, and PancakeSwap subgraphs.

Supported features:

- configured-pool loading through the official v2 and v3 factories;
- optional discovery of leading pools through chain-specific subgraphs;
- ERC-20 metadata, token balances, and native-token balances through
  JSON-RPC;
- Level1 bid and ask probes based on exact-input and exact-output contract
  quotes;
- historical and polled realtime v2 and v3 swaps;
- historical and updating OHLCV candles aggregated from swaps;
- exact-input and exact-output swaps through the official v2 and v3 routers;
- direct ERC-20 allowance, reset, and approval transactions;
- EIP-1559 and legacy transaction signing, broadcast, receipt tracking, gas
  commissions, executed-amount decoding from v2/v3 Swap events, and session
  order-status subscriptions.

An HTTP JSON-RPC endpoint and wallet address are required. A private key is
required only for trading. The Graph gateway key is optional when an absolute
subgraph endpoint is configured; it is required for deployment IDs served by
The Graph gateway. The defaults target BNB Smart Chain v3. PancakeSwap's BSC
v2 subgraph is available through NodeReal rather than a default The Graph
deployment, so it must be configured explicitly when v2 history or discovery
is needed on BSC. On other chains, configure explicit markets or a compatible
subgraph when no chain default is available.

The connector works with direct two-token v2 and v3 pools. It intentionally
does not imitate the PancakeSwap web application's Smart Router: multi-hop,
stable-swap, Infinity, and other aggregated routes are outside its scope.
Native assets must be represented by their wrapped ERC-20 contracts for pool
quotes and swaps. Robinhood Chain has no official direct v3 SwapRouter address;
the connector therefore allows v3 market data there but rejects direct v3
trading. V2 trading remains available where the official v2 router is
deployed.

PancakeSwap is an automated market maker and has no discrete exchange order
book. The connector publishes executable quote probes as Level1 data and does
not synthesize market-depth levels. Transactions submitted outside the current
adapter session cannot be reconstructed as StockSharp orders from a receipt
alone and are not presented as such.

Every GraphQL and JSON-RPC payload is represented by a concrete DTO. The
transport does not use dynamic JSON trees, anonymous protocol objects,
protocol dictionaries, or untyped object arrays.

Official resources:

- [PancakeSwap developer documentation](https://developer.pancakeswap.finance/)
- [Subgraph API](https://developer.pancakeswap.finance/apis/subgraph)
- [V2 deployment addresses](https://developer.pancakeswap.finance/contracts/v2/addresses)
- [V3 deployment addresses](https://developer.pancakeswap.finance/contracts/v3/addresses)
- [V3 SwapRouter interface](https://github.com/pancakeswap/pancake-v3-contracts/blob/main/projects/v3-periphery/contracts/interfaces/ISwapRouter.sol)
- [V3 QuoterV2 interface](https://github.com/pancakeswap/pancake-v3-contracts/blob/main/projects/v3-periphery/contracts/interfaces/IQuoterV2.sol)
