# Uniswap Connector

The connector integrates Uniswap AMM markets through the current Uniswap
Trading API, EVM JSON-RPC, and the Uniswap v3 subgraph.

Supported features:

- configured-pool loading and optional discovery of the leading Uniswap v3
  pools through The Graph;
- ERC-20 and native-token metadata and wallet balances through JSON-RPC;
- Level1 bid and ask probes based on exact-output and exact-input Trading API
  quotes;
- historical and polled realtime v3 swaps;
- historical and updating OHLCV candles aggregated from v3 swaps;
- market swaps through the official quote, approval, and swap transaction
  flow;
- direct ERC-20 approval transactions, EIP-1559 and legacy transaction
  signing, transaction broadcast, receipt tracking, and gas commissions;
- session order-status subscriptions for transactions submitted by this
  connector.

The Uniswap Trading API key, an HTTP JSON-RPC endpoint, and a wallet address
are required. A private key is required only for trading. The Graph gateway
key is optional and enables v3 pool discovery, swaps, and candles. The default
subgraph and configured pools target Ethereum Mainnet; other chains require a
chain-specific subgraph deployment and explicit pool definitions.
Tempo has no native token, so the connector intentionally omits the synthetic
`eth_getBalance` value and does not report a native-gas commission for that
chain.

Uniswap is an automated market maker and has no discrete exchange order book.
The connector therefore publishes executable quote probes as Level1 data and
does not synthesize market-depth levels. Transactions submitted outside the
current adapter session cannot be reconstructed as StockSharp orders from a
receipt alone and are not presented as such.

Every REST, GraphQL, and JSON-RPC payload is represented by a concrete DTO.
The transport does not use dynamic JSON trees, anonymous protocol objects,
protocol dictionaries, or untyped object arrays.

Official resources:

- [Uniswap Trading API getting started](https://developers.uniswap.org/docs/trading/swapping-api/getting-started)
- [Trading API integration guide](https://developers.uniswap.org/docs/trading/swapping-api/integration-guide)
- [Supported chains](https://developers.uniswap.org/docs/trading/swapping-api/supported-chains)
- [Quote API](https://developers.uniswap.org/docs/api-reference/aggregator_quote)
- [Approval API](https://developers.uniswap.org/docs/api-reference/check_approval)
- [Swap transaction API](https://developers.uniswap.org/docs/api-reference/create_swap_transaction)
- [Uniswap subgraphs](https://developers.uniswap.org/docs/ecosystem/subgraphs/overview)
