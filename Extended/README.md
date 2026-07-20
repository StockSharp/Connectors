# Extended connector for StockSharp

The connector integrates the current official Extended Starknet REST v1 and
WebSocket RPC v2 APIs for spot and perpetual markets.

Supported functionality:

- market discovery and current market statistics;
- live Level1, sequence-checked full order-book updates, and public trades;
- recent public trades, historical candles, and live candles;
- authenticated balances, spot balances, positions, orders, and fills;
- Starknet SNIP-12 signed limit, market, and conditional order entry;
- cancellation, mass cancellation, and atomic cancel-and-replace;
- mainnet and official Sepolia testnet endpoints.

Public market data requires no credentials. `Key` enables read-only account
data. Trading additionally requires the Stark private key belonging to the L2
key returned by Extended for the active account. The connector reads the
account vault and public Stark key from the authenticated account endpoint and
validates the private key before enabling order entry.

All wire messages use concrete DTOs. The connector does not use dynamic JSON
trees, anonymous protocol payloads, or protocol dictionaries.

Official resources:

- [Extended API documentation](https://api.docs.extended.exchange/)
- [Official Extended Python SDK](https://github.com/x10xchange/python_sdk)
- [Official Extended Stark crypto wrapper](https://github.com/x10xchange/stark-crypto-wrapper-py)
- [Extended brand kit](https://docs.extended.exchange/extended-resources/more/brand-kit)
- [StockSharp Extended connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/extended.html)

Extended and its marks are trademarks of their respective owner. StockSharp
is not affiliated with or endorsed by Extended.
