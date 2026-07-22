# Ostium connector for StockSharp

The connector integrates the current official Ostium Builder API, subgraph,
price WebSocket, and Arbitrum contracts for on-chain perpetual markets covering
crypto, currencies, commodities, indices, equities, and ETFs.

Supported functionality:

- discovery of all listed Ostium perpetual markets and their current risk
  parameters;
- realtime oracle/index Level1 data over the official Builder WebSocket;
- historical and continuously updated 1-minute, 5-minute, 15-minute, 1-hour,
  4-hour, and daily candles from the official OHLC API;
- read-only USDC and ETH balances, open positions, active limit/stop orders,
  order history, and fills for an EVM wallet;
- signed market, limit, and stop orders on Arbitrum;
- limit/stop replacement and cancellation, including filtered group
  cancellation;
- full and partial position closing;
- configurable automatic USDC approval for Ostium TradingStorage;
- Arbitrum One and Arbitrum Sepolia environments with chain-ID validation.

Public market discovery, Level1 data, and candle history require no credentials.
`WalletAddress` enables read-only account data. Trading additionally requires
the corresponding EVM `PrivateKey`. Custom JSON-RPC and subgraph endpoints may
be supplied; otherwise the connector uses the official network defaults.

Ostium defines order volume as USDC collateral, not leveraged base quantity.
The effective notional is the collateral multiplied by `Leverage` in
`OstiumOrderCondition`. Close requests use `PositionIndex`; `ClosePercentage`
may be used for an explicit partial close. Without it, the requested volume is
interpreted as collateral to close and converted into a percentage.

The public price stream is an oracle quote feed. It is exposed as bid, ask, and
index Level1 data and is deliberately not presented as a trade tape. Ostium
does not publish a native order book, so the connector does not synthesize one.
The OHLC endpoint does not publish volume; candle volume is therefore zero.
Unrealized position PnL is calculated from the current oracle mid and base
position size and excludes projected rollover charges.

Official resources:

- [Ostium documentation](https://docs.ostium.com/)
- [Builder API documentation](https://docs.ostium.com/developer/builder-api/overview)
- [SDK overview](https://docs.ostium.com/developer/sdk/overview)
- [Official Ostium contracts](https://github.com/0xOstium/smart-contracts-public)
- [Official Python SDK](https://github.com/0xOstium/ostium-python-sdk)
- [StockSharp Ostium connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/ostium.html)

Ostium and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Ostium.
