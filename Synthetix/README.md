# Synthetix connector for StockSharp

The connector integrates the current Synthetix perpetual-futures exchange on
Ethereum mainnet through its official off-chain REST and WebSocket API.

Supported functionality:

- discovery of perpetual markets and their trading constraints;
- Level1 prices and market statistics;
- full order-book snapshots followed by realtime updates;
- recent and realtime public trades;
- historical and realtime candles for the API's fixed-duration intervals;
- subaccount collateral, margin, positions, active orders, order history, and
  fills;
- private order, fill, margin, and position updates over WebSocket;
- market, limit, stop-market, stop-limit, take-profit-market, and
  take-profit-limit orders;
- order replacement, individual cancellation, and cancellation of all open
  orders.

Public market data does not require credentials. Account access and trading
require an EVM private key and an existing Synthetix subaccount. Configure the
numeric `SubAccountId`; the corresponding key is used locally to produce
EIP-712 signatures and is never sent to the API. Trading uses collateral that
has already been deposited into the subaccount.

`SynthetixOrderCondition` exposes trigger price, trigger direction,
trigger-market execution, reduce-only behavior, and close-position behavior.
StockSharp transaction identifiers are converted deterministically to the
16-byte client order identifiers required by Synthetix.

The connector targets the current Ethereum-mainnet API. The public recent
trade endpoint returns at most 100 records. Private history endpoints accept
at most 1,000 records and a seven-day time range per request; the connector
normalizes larger requests to that supported window. Deposits, withdrawals,
subaccount creation, collateral transfers, and TWAP orders are intentionally
outside the connector. Fill-or-kill orders are not exposed because the API
does not provide that time-in-force mode.

Official resources:

- [Synthetix developer documentation](https://developers.synthetix.io/)
- [API authentication](https://developers.synthetix.io/developer-resources/api/authentication)
- [Public market data API](https://developers.synthetix.io/developer-resources/api/rest-api/info)
- [Trading API](https://developers.synthetix.io/developer-resources/api/rest-api/trade)
- [Synthetix user documentation](https://docs.synthetix.io/)
- [StockSharp Synthetix connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/synthetix.html)

Synthetix and its marks are trademarks of their respective owner. StockSharp
is not affiliated with or endorsed by Synthetix.
