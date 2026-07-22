# CoW Protocol Connector

The connector integrates the official CoW Protocol Order Book API and
Settlement contracts. It supports Ethereum, Gnosis, Arbitrum, Base,
Avalanche, Polygon, and BNB Smart Chain without an API key.

Supported features:

- configured ERC-20 markets with on-chain token metadata validation;
- verified executable bid and ask quotes from the Order Book API;
- public trades from actual `Trade` events emitted by the official Settlement
  contract;
- historical and updating OHLCV candles aggregated from those events;
- native and configured-token wallet balances;
- market and limit orders with local EIP-712 signing;
- signed single-order cancellation and account order history;
- actual fills, dynamic protocol fees, and settlement transaction references;
- allowance checks and optional locally signed ERC-20 approvals through the
  official VaultRelayer.

`Markets` contains semicolon-separated definitions in
`base-token|quote-token|security-code` format. The security code is optional.
When the setting is empty, the connector uses a liquid wrapped-native/stable
pair for the selected production network. CoW Protocol orders require ERC-20
tokens, so native assets must use their wrapped form.

`ApiEndpoint` and `RpcEndpoint` can override the official production API and
the default public HTTP JSON-RPC endpoint. Public RPC services are rate
limited. `HistoryBlockRange` bounds each `eth_getLogs` request, while
`HistoryBlockCount` bounds history requests without an explicit start time.

A wallet is optional for public quotes and market data. A wallet address is
required for portfolio and account-order data. The matching private key is
required for order submission, cancellation, and automatic approvals. The
private key remains local. Orders are signed with the CoW Protocol EIP-712
domain (`Gnosis Protocol`, version `v2`) and are submitted only after the
connector verifies the chain, Settlement contract, domain separator, and
VaultRelayer address.

The Order Book API does not provide a public WebSocket stream. The connector
therefore polls official REST order state and overlapping Settlement log
ranges; it does not synthesize trades, depth, or socket events.

Official resources:

- [CoW Protocol documentation](https://docs.cow.fi/)
- [Order Book API reference](https://api.cow.fi/docs/)
- [CoW Protocol contracts](https://github.com/cowprotocol/contracts)
- [CoW Protocol services](https://github.com/cowprotocol/services)
- [CoW Protocol app data](https://github.com/cowprotocol/app-data)
