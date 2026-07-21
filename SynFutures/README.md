# SynFutures connector for StockSharp

The connector integrates SynFutures V3 (Oyster AMM) perpetual markets on Base
through the current public REST API, public WebSocket, Base JSON-RPC, and the
deployed instrument contracts.

Supported functionality:

- discovery of active perpetual markets with token, price, volume, open
  interest, funding, TVL, and leverage metadata;
- realtime Level1 market statistics, full order-book snapshots, and public
  trades over the official WebSocket;
- historical and continuously updated 1-minute, 5-minute, 15-minute,
  30-minute, 1-hour, 4-hour, daily, and weekly candles;
- read-only Gate collateral balances, Base ETH balance, positions, active
  limit orders, order history, and fills for an EVM wallet;
- directly signed market and limit orders through each market's instrument
  contract;
- exact on-chain limit-order identifiers derived from instrument, expiry,
  tick, and nonce;
- individual and filtered group cancellation, with up to eight price ticks in
  each contract call;
- replacement implemented as an on-chain cancellation followed by a new
  limit placement;
- WebSocket portfolio notifications with polling as a fallback.

Public market data requires no user credential. `WalletAddress` enables
read-only account data. Trading additionally requires the corresponding EVM
`PrivateKey`. The connector validates that JSON-RPC is connected to Base chain
8453 and that the configured wallet matches the signing key.

Order volume is base-asset quantity and is converted to the protocol's 18
decimal WAD representation. `SynFuturesOrderCondition.Leverage` controls the
margin calculated for new exposure. `Margin` may override that calculation;
zero margin is used automatically for explicit close-only market orders.
Market-order protection ticks are calculated from the current on-chain
quotation and configured slippage. Limit prices are aligned to the Base
deployment's five-tick order spacing.

SynFutures trading consumes collateral already deposited in the protocol Gate.
The connector deliberately does not move user tokens or issue unlimited token
approvals automatically. If collateral is insufficient, deposit it through
the SynFutures application before sending an order.

All wire messages use concrete DTOs. The implementation does not use dynamic
JSON trees, anonymous protocol objects, or protocol dictionaries.

Official resources:

- [SynFutures documentation](https://docs.synfutures.com/)
- [SynFutures application](https://app.synfutures.com/)
- [Official TypeScript SDK](https://github.com/SynFutures/ts-sdk)
- [Official Oyster SDK](https://github.com/SynFutures/oyster-sdk)
- [Official Oyster subgraph](https://github.com/SynFutures/oyster-subgraph)
- [StockSharp SynFutures connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/synfutures.html)

SynFutures and its marks are trademarks of their respective owner. StockSharp
is not affiliated with or endorsed by SynFutures.
