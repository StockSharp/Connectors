# Gains Network gTrade connector for StockSharp

The connector integrates Gains Network gTrade perpetual markets through the
current chain-specific backend, pricing REST service, pricing WebSocket, and
GNSMultiCollatDiamond contracts.

Supported functionality:

- discovery of crypto, forex, commodity, index, and stock perpetual markets;
- realtime mark and index prices over WebSocket;
- current Level1 bid, ask, mark, index, and OHLC snapshot values;
- collateral balances and individual leveraged positions;
- active limit and stop-entry orders plus personal execution history;
- market, limit, and stop-entry registration;
- pending-entry replacement, individual pending-entry cancellation, and filtered
  pending-entry group cancellation;
- full market close of an existing position;
- Arbitrum One, Base, and Polygon PoS deployments.

Public market discovery and Level1 data do not require credentials. Account
data requires an EVM wallet address. Trading additionally requires the matching
private key, which is used locally to sign chain transactions and is never sent
to Gains backend services. A custom JSON-RPC endpoint can be configured when a
public network endpoint is unsuitable for production traffic.

Order volume is the amount of the collateral token, not the leveraged notional
or base-asset quantity. `GainsNetworkOrderCondition` selects collateral,
leverage, take-profit and stop-loss prices, stop-entry behavior, and the
on-chain trade index used for management and closing. The adapter can approve
the selected collateral token for the Gains diamond when its allowance is too
small. The current contract close operation exposed by the connector closes a
whole position; partial position changes are intentionally not synthesized.

Gains does not expose a central-limit order book or a public trade tape through
these APIs. The `/charts` endpoint is a current OHLC snapshot, not historical
OHLCV storage, so the connector does not advertise market depth, public ticks,
or historical candles. Live prices use the documented v4 frame and also
accept the legacy flat-array frame currently returned by some pricing nodes.

Official resources:

- [Gains integrator documentation](https://docs.gains.trade/developer/integrators)
- [Backend integration](https://docs.gains.trade/developer/integrators/backend)
- [Live prices and OHLC snapshots](https://docs.gains.trade/developer/integrators/price-feed)
- [Trading contracts](https://docs.gains.trade/developer/integrators/trading-contracts)
- [Backend API reference](https://docs.gains.trade/developer/api-reference/introduction)
- [Backend wire types](https://docs.gains.trade/developer/technical-reference/backend/backend-types)
- [StockSharp Gains Network connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/gains_network.html)

Gains Network, gTrade, and their marks are trademarks of their respective
owner. StockSharp is not affiliated with or endorsed by Gains Network.
