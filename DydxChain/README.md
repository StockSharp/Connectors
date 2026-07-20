# dYdX Chain connector for StockSharp

The connector integrates the current dYdX Chain Indexer and validator APIs for
decentralized perpetual futures.

Supported functionality:

- discovery of active and suspended perpetual markets with their current tick,
  size, margin, funding, volume, and open-interest parameters;
- live Level1, incremental order books, public trades, and candle updates over
  the official Indexer WebSocket protocol;
- REST history for trades and candles;
- public subaccount equity, collateral, asset balances, perpetual positions,
  orders, and fills;
- direct Cosmos `MsgPlaceOrder` and `MsgCancelOrder` transactions signed with a
  secp256k1 private key and broadcast to dYdX Chain;
- short-term market and IOC orders, long-term limit and post-only orders,
  conditional stop-loss and take-profit orders, reduce-only orders, and native
  TWAP orders;
- order replacement through an explicit cancel-and-place sequence and filtered
  group cancellation.

Public market data requires no credentials. `WalletAddress` enables read-only
subaccount data. Trading additionally requires the corresponding hexadecimal
Cosmos secp256k1 `PrivateKey`. The address is derived from the key and checked
before use. `SubaccountNumber` selects the dYdX subaccount.

The connector checks that the validator reports chain ID `dydx-mainnet-1`.
Market orders are submitted as short-term IOC orders with a configurable oracle
price protection limit. Stateful and conditional orders use UTC expiration
timestamps; short-term orders use the current chain height.

All REST and WebSocket messages use concrete DTOs. Keyed objects returned by the
Indexer are decoded by streaming converters into typed arrays. The connector
does not use dynamic JSON trees, anonymous protocol payloads, or protocol
dictionaries.

Official resources:

- [dYdX documentation](https://docs.dydx.xyz/)
- [API endpoints](https://docs.dydx.xyz/interaction/endpoints)
- [Indexer WebSocket API](https://docs.dydx.xyz/indexer-client/websockets)
- [Official dYdX v4 clients](https://github.com/dydxprotocol/v4-clients)
- [Official dYdX Chain source](https://github.com/dydxprotocol/v4-chain)
- [StockSharp dYdX Chain connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/dydx_chain.html)

dYdX and its marks are trademarks of their respective owner. StockSharp is not
affiliated with or endorsed by dYdX.
