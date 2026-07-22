# 1inch Connector

The connector integrates the official 1inch Classic Swap API (Pathfinder
v6.1) and EVM JSON-RPC. A valid 1inch Business Portal API key is required.

Supported features:

- configured ERC-20 markets with on-chain token metadata validation;
- executable bid and ask quotes polled from the official `/quote` endpoint;
- native-asset and configured-token wallet balances;
- immediate market swaps constructed by the official `/swap` endpoint;
- local EVM signing and broadcasting through the configured JSON-RPC node;
- allowance checks and optional locally signed ERC-20 approvals;
- receipt tracking, gas commission, and actual fills decoded from wallet
  `Transfer` events.

The connector supports Ethereum, Optimism, BNB Smart Chain, Gnosis Chain,
Polygon PoS, Base, Arbitrum One, Avalanche C-Chain, and Linea. `Markets`
contains semicolon-separated definitions in
`base-token|quote-token|security-code` format; the security code is optional.
When the setting is empty, a liquid wrapped-native/stable pair is used for the
selected network. Markets intentionally use wrapped ERC-20 assets rather than
native-token aliases so approvals and receipt accounting remain unambiguous.

Classic Swap accepts an exact source-token amount. A sell therefore maps
directly to an exact base-token input. For a buy, the connector obtains a
reverse executable quote to estimate the quote-token input. The final bought
volume and price always come from the mined transaction's token transfers, not
from the estimate.

`ApiEndpoint` and `RpcEndpoint` can override the official production API and
the default public HTTP JSON-RPC endpoint. Public RPC services and the free API
tier are rate limited. The private key remains local and is never sent to
1inch; it is needed only for swaps and automatic approvals.

Classic Swap does not provide a public trade or order-book WebSocket. The
connector polls executable REST quotes and on-chain receipts. It does not
synthesize trades, candles, depth, history, or socket events. Order-status
queries cover swaps submitted during the current connector session because
Classic Swap is a transaction builder rather than an account order store.

Official resources:

- [1inch Business Portal documentation](https://business.1inch.com/portal/documentation/)
- [Classic Swap introduction](https://business.1inch.com/portal/documentation/apis/swap/classic-swap/introduction)
- [Classic Swap quick start](https://business.1inch.com/portal/documentation/apis/swap/classic-swap/quick-start)
- [1inch GitHub organization](https://github.com/1inch)
