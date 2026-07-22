# Aerodrome Finance Connector

The connector integrates Aerodrome Finance pools on Base through the
official on-chain contracts and standard Base JSON-RPC transports.

Supported features:

- configured-pool loading with contract-code validation;
- automatic recognition of classic volatile, classic stable, and Slipstream
  concentrated-liquidity pools;
- validation against the official classic factory and all three published
  Slipstream factory generations;
- ERC-20 metadata and configured-token balances;
- executable bid and ask quote probes;
- historical swaps from \`eth_getLogs\`;
- realtime swaps from \`eth_subscribe\` WebSocket log notifications, with an
  overlapping HTTP JSON-RPC polling fallback;
- historical and updating OHLCV candles aggregated from actual swap events;
- native ETH and configured-token portfolio balances;
- direct classic and Slipstream swaps, ERC-20 approvals, EIP-1559 or legacy
  transaction signing, receipt tracking, commissions, and fill decoding.

Set \`Pools\` to semicolon-separated pool addresses. A pool may optionally use
\`pool|base-token|quote-token|security-code\` to specify orientation and a
stable StockSharp security identifier. The connector reads token addresses,
pool type, factory, and Slipstream tick spacing from each pool contract.
Aerodrome does not publish one bounded official market-index endpoint that is
appropriate for enumerating every historical and current deployment, so the
connector does not pretend to discover an exhaustive pool list.

The defaults include the official Base public HTTP and WebSocket endpoints
and established classic and Slipstream pools. Production systems should
dedicated Base RPC endpoints with the log-range and WebSocket capacity their
workload requires. \`HistoryBlockRange\` limits each \`eth_getLogs\` request, and
\`HistoryBlockCount\` bounds a history request that does not provide a start
time.

A wallet is optional for public market data. A public wallet address is
required for portfolio data, and a matching private key is required only for
trading. Direct pools use wrapped WETH rather than native ETH. Classic pools
support exact-input swaps; for a StockSharp buy volume, the connector derives
the required input from the pool's official \`getAmountOut\` function and uses
the configured slippage as the maximum-input allowance. Slipstream uses its
official exact-input and exact-output router methods.

Aerodrome is an automated market maker and has no discrete exchange order
book. The connector publishes executable quote probes as Level1 data and
does not synthesize depth. Transactions submitted outside the current
adapter session remain available as market-data events but are not
reconstructed as StockSharp orders.

Official resources:

- [Aerodrome documentation](https://aerodrome.finance/docs)
- [Official deployment and security information](https://aerodrome.finance/security)
- [Classic pool and router contracts](https://github.com/aerodrome-finance/contracts)
- [Slipstream contracts and deployment addresses](https://github.com/aerodrome-finance/slipstream)
- [Base network documentation](https://docs.base.org/base-chain/quickstart/connecting-to-base)
- [Ethereum JSON-RPC specification](https://ethereum.org/en/developers/apis/json-rpc/)
