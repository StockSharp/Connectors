# StockSharp Balancer Connector

The connector integrates StockSharp with direct Balancer V2 and V3 liquidity
pools on supported EVM networks. It uses the official Balancer GraphQL API for
pool discovery, historical swaps, and executable SOR quotes; EVM JSON-RPC for
balances and transactions; and WebSocket `eth_subscribe` for live Vault swap
events.

## Access

Public pool, quote, and market data do not require an API key. Default HTTP and
WebSocket URLs are public RPC endpoints and can impose rate, connection, or log
limits. Production systems should configure dedicated RPC endpoints for the
selected network.

A wallet address is required for portfolio balances. Trading additionally
requires its private key. The connector signs EVM transactions locally; the key
is never sent to Balancer, the Balancer API, or the RPC provider.

## Supported networks

- Ethereum;
- Arbitrum One;
- Base;
- Optimism;
- Polygon PoS (Balancer V2);
- Gnosis Chain;
- Avalanche C-Chain.

## Supported operations

- high-TVL pool discovery through the official Balancer API;
- explicit V2 pool IDs and V3 pool addresses;
- weighted, stable, Gyroscope, ReClamm, and other directly tradable pool types
  returned by the API;
- one StockSharp security for every direct token pair in a selected pool;
- executable bid and ask Level1 probes from direct, single-pool SOR paths;
- historical swaps from `poolEvents`;
- live V2 and V3 `Swap` events through Vault log subscriptions, with GraphQL
  polling as an overlapping fallback and automatic WebSocket reconnect;
- time-frame candles aggregated from actual swaps;
- native-asset and discovered ERC-20 wallet balances;
- immediate market sells and buys, local EIP-1559 or legacy signing, receipt
  tracking, transaction orders, fills, and gas commissions;
- V2 Vault ERC-20 approvals and the complete V3 ERC-20/Permit2/Router allowance
  flow.

`Pools` accepts semicolon-separated entries in one of these forms:

- `pool` to expose every direct token pair;
- `pool|base-token|quote-token` to expose one oriented pair;
- `pool|base-token|quote-token|security-code` to set its StockSharp code.

`MaximumDiscoveredPools` limits automatic discovery and `MinimumPoolTvl`
filters it by USD TVL. Explicit pools are loaded independently of these limits.
Pool and token contracts are validated on-chain, and API token decimals must
match the ERC-20 contracts.

## Important boundaries

Balancer is an AMM and has no central limit-order book. The connector publishes
executable quote probes as Level1 values and does not fabricate resting orders
or market depth. Swaps are immediate market operations and cannot be cancelled,
replaced, made post-only, or assigned a time in force.

Only direct paths through the configured pool are accepted. SOR responses that
contain another pool, an intermediate token, or a boosted-pool buffer are
rejected. Multi-pool routing, native/WETH conversion, liquidity provision,
staking, gauges, governance, and relayer batch operations are outside this
connector. ERC-20 and wrapped-native tokens remain tradable.

Historical requests use the bounded API event feed and return at most
`HistoryMaximum` swaps. Order-status history covers transactions submitted
during the current adapter session because an AMM swap does not create a
persistent exchange order.

Every HTTP, JSON-RPC, and WebSocket payload is represented by a concrete DTO.
The connector does not use dynamic JSON trees, anonymous protocol objects,
protocol dictionaries, or untyped object arrays.

## Official documentation

- [Balancer developer documentation](https://docs.balancer.fi/)
- [Balancer API documentation](https://docs.balancer.fi/data-and-analytics/data-and-analytics/balancer-api/balancer-api.html)
- [Balancer SOR query API](https://docs.balancer.fi/data-and-analytics/data-and-analytics/balancer-api/swap-query-sor.html)
- [Balancer pool swap events](https://docs.balancer.fi/data-and-analytics/data-and-analytics/balancer-api/pool-swap-events.html)
- [Balancer V3 swaps](https://docs.balancer.fi/integration-guides/aggregators/making-and-querying-swaps.html)
- [Balancer deployment addresses and ABIs](https://github.com/balancer/balancer-deployments)
- [Balancer backend GraphQL schema](https://github.com/balancer/backend)
- [Permit2 documentation](https://docs.uniswap.org/contracts/permit2/overview)
- [Ethereum JSON-RPC specification](https://ethereum.org/en/developers/apis/json-rpc/)
