# Nado connector for StockSharp

The connector integrates the current official Nado gateway v1/v2, archive
indexer v1/v2, and subscription WebSocket APIs for spot and perpetual markets
on Ink.

Supported functionality:

- spot and perpetual market discovery with trading increments;
- live Level1, public trades, and sequence-checked order books;
- recent trades, historical candles, and live candles;
- public subaccount balances, positions, open orders, order history, and fills;
- EIP-712 signed limit, protected market, IOC, FOK, and post-only orders;
- reduce-only and isolated-margin order appendix fields;
- individual, filtered, product-wide, and atomic cancel-and-replace operations;
- configurable official mainnet or testnet gateway, archive, and WebSocket URLs.

Public market data requires no credentials. `WalletAddress` enables read-only
subaccount data because Nado account streams and queries use the public bytes32
subaccount identifier. Trading additionally requires the corresponding EVM
`PrivateKey`. The default subaccount name is `default`.

All wire messages use concrete DTOs. The connector does not use dynamic JSON
trees, anonymous protocol payloads, or protocol dictionaries.

Official resources:

- [Nado API documentation](https://docs.nado.xyz/developer-resources/api)
- [Nado API endpoints](https://docs.nado.xyz/developer-resources/api/endpoints)
- [Official Nado TypeScript SDK](https://github.com/nadohq/nado-typescript-sdk)
- [Official Nado Python SDK reference](https://nadohq.github.io/nado-python-sdk/api-reference.html)
- [StockSharp Nado connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/nado.html)

Nado and its marks are trademarks of their respective owner. StockSharp is not
affiliated with or endorsed by Nado.
