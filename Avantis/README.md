# Avantis connector for StockSharp

The connector integrates the current official Avantis APIs and Base Mainnet
contracts for cross-asset perpetual markets.

Supported functionality:

- discovery of listed crypto, forex, commodity, equity, and index markets with
  their current risk and trading parameters;
- live Level1 prices through the Pyth Lazer stream with the official Hermes
  fallback for markets not available on Lazer;
- public Base-chain connectivity and wallet balance reporting;
- current USDC and ETH balances, open positions, and pending limit orders;
- signed market, zero-fee market, limit, and stop-limit orders;
- limit-order replacement and cancellation;
- full and partial position closing;
- configurable automatic USDC approval for the Avantis trading contract.

Public market discovery and Level1 data require no credentials. `WalletAddress`
enables read-only portfolio and order data. Trading additionally requires the
corresponding Base-compatible EVM `PrivateKey`. The connector verifies that the
configured JSON-RPC endpoint is connected to Base Mainnet (chain ID 8453).

Avantis defines order volume as USDC collateral, not leveraged position size.
The effective position value is the collateral multiplied by the leverage in
`AvantisOrderCondition`. Avantis does not expose a native public order book,
public trade tape, or candle-history API, so the connector does not synthesize
those data types.

Official resources:

- [Avantis documentation](https://docs.avantisfi.com/)
- [Avantis SDK documentation](https://sdk.avantisfi.com/)
- [Official Avantis Trader SDK](https://github.com/Avantis-Labs/avantis_trader_sdk)
- [Avantis brand kit](https://docs.avantisfi.com/brand/avantis-brand-kit)
- [StockSharp Avantis connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/avantis.html)

Avantis and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Avantis.
