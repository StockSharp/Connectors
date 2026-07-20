# Fluid DEX Connector

The connector integrates Fluid DEX T1 pools through the official on-chain
factory, reserves resolver, and pool contracts using standard EVM JSON-RPC.

Supported features:

- Ethereum, Arbitrum One, Base, BNB Smart Chain, Polygon PoS, and Plasma
  deployments;
- automatic pool discovery through the official factory resolver;
- optional configured pool selection and explicit base/quote orientation;
- ERC-20 and native-token metadata and wallet balances;
- executable bid and ask quote probes through `estimateSwapIn` and
  `estimateSwapOut`;
- executable multi-level quote ladders exposed as market-depth snapshots;
- historical swaps from `eth_getLogs`;
- real-time swaps from `eth_subscribe` WebSocket log notifications, with an
  overlapping HTTP JSON-RPC polling fallback;
- historical and updating OHLCV candles aggregated from actual `Swap` events;
- direct exact-input and exact-output T1 pool swaps, native-token value,
  ERC-20 approvals, EIP-1559 or legacy transaction signing, receipt tracking,
  gas commissions, and fill decoding.

Leave `Pools` empty to load up to `MaximumDiscoveredPools` pools from the
official resolver. To limit the connector to selected pools, use
semicolon-separated pool addresses. An item may use
`pool|base-token|quote-token|security-code` to set orientation and a stable
StockSharp security identifier. `FactoryAddress` and `ResolverAddress` default
to the official deterministic deployments and are configurable for a future
official migration.

The connector selects a public HTTP endpoint and, where available, a public
WebSocket endpoint for the chosen chain. Production systems should configure
dedicated endpoints with sufficient log-range, request-rate, and subscription
capacity. Plasma has no built-in WebSocket endpoint; configure one explicitly
to enable push delivery there. `HistoryBlockRange` limits each `eth_getLogs`
request, and `HistoryBlockCount` bounds history requests without a start time.

A wallet is optional for public market data. A public wallet address is
required for portfolio data, and a matching private key is required only for
trading. Buy volume maps to an exact-output swap; sell volume maps to an
exact-input swap. The configured slippage is applied as the maximum input or
minimum output. Fluid DEX swaps are immediate on-chain market operations, so
the protocol has no cancellable or replaceable exchange-side orders.

Fluid DEX is an automated market maker, not a discrete central-limit order
book. Level1 and depth are executable quote probes. Each depth level is the
marginal price between successive cumulative `ProbeVolume` simulations; it is
not presented as resting order liquidity. Candles and ticks, by contrast, are
derived only from confirmed on-chain `Swap` events. Transactions submitted
outside the current adapter session are visible as market-data events but are
not reconstructed as StockSharp orders.

Every HTTP JSON-RPC and WebSocket payload is represented by a concrete DTO.
The transport does not use dynamic JSON trees, anonymous protocol objects,
protocol dictionaries, or untyped object arrays.

Official resources:

- [Fluid technical documentation](https://docs.fluid.instadapp.io/)
- [Fluid public contracts](https://github.com/Instadapp/fluid-contracts-public)
- [Fluid DEX T1 interface](https://github.com/Instadapp/fluid-contracts-public/blob/main/contracts/protocols/dex/interfaces/iDexT1.sol)
- [Fluid DEX reserves resolver](https://github.com/Instadapp/fluid-contracts-public/blob/main/contracts/periphery/resolvers/dexReserves/main.sol)
- [Official deployment artifacts](https://github.com/Instadapp/fluid-contracts-public/tree/main/deployments)
- [Ethereum JSON-RPC specification](https://ethereum.org/en/developers/apis/json-rpc/)
