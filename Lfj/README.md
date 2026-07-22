# LFJ / Trader Joe Connector

The connector integrates LFJ Liquidity Book V2.2 pools on Avalanche C-Chain
through the official on-chain contracts and standard Avalanche JSON-RPC
transports. It does not require an LFJ API key.

Supported features:

- configured-pool loading with contract-code validation;
- V2.2 pool verification against the official LFJ factory and its active-route
  metadata;
- ERC-20 token metadata and wallet balances;
- executable bid and ask probes through the pool's `getSwapOut` and
  `getSwapIn` methods;
- historical swaps from `eth_getLogs`;
- realtime swaps from `eth_subscribe` WebSocket log notifications, with
  overlapping HTTP JSON-RPC polling as a fallback;
- historical and updating OHLCV candles aggregated from actual swap events;
- native AVAX and configured-token portfolio balances;
- exact-input sells and exact-output buys through the official V2.2 router;
- ERC-20 approvals, local EVM signing, EIP-1559 or legacy fees, receipt
  tracking, commissions, and fill decoding.

Set `Pools` to semicolon-separated V2.2 pair addresses. A pair may optionally
use `pool|base-token|quote-token|security-code` to pin its orientation and
StockSharp security identifier. The connector reads the factory, token X,
token Y, and bin step directly from every pair. It rejects a pair that the
official factory does not expose as an active V2.2 route.

The default configuration uses Avalanche's public HTTP and WebSocket
endpoints and the active WAVAX/USDC bin-step-10 pair. Public endpoints are
rate-limited; production workloads can supply dedicated endpoints with the
required log range and WebSocket capacity. `HistoryBlockRange` limits each
`eth_getLogs` call, while `HistoryBlockCount` bounds requests without an
explicit start time.

A wallet is optional for public market data. A public wallet address is
required for portfolio data, and the matching private key is required only
for trading. The private key stays local and is used solely to sign Avalanche
C-Chain transactions. Direct pool routes use wrapped WAVAX rather than native
AVAX.

LFJ is an automated market maker and has no discrete exchange order book. The
connector publishes executable quote probes as Level1 data and does not
synthesize market depth. Transactions submitted outside the current adapter
session remain visible as market-data events but are not reconstructed as
StockSharp orders.

Official resources:

- [LFJ developer documentation](https://developers.lfj.gg/)
- [Avalanche deployment addresses](https://developers.lfj.gg/deployment-addresses/avalanche)
- [LFJ trade guide](https://developers.lfj.gg/sdk/trade)
- [Liquidity Book V2 contracts](https://github.com/lfj-gg/joe-v2)
- [LFJ SDKs](https://github.com/lfj-gg/joe-sdks)
- [Avalanche C-Chain API](https://build.avax.network/docs/rpcs/c-chain)
