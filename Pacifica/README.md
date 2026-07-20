# Pacifica connector for StockSharp

The connector integrates the current official Pacifica mainnet REST and
WebSocket APIs for perpetual futures.

Supported functionality:

- market discovery and current price statistics through REST;
- live Level1, complete 10-level order-book snapshots, and public trades;
- recent public trades and historical candles through REST;
- streaming candles for all fixed intervals published by Pacifica;
- read-only account balance, spot collateral, positions, orders, and fills
  using a public Solana account address;
- optional Ed25519-signed limit and market order entry, cancellation,
  cancel-all, and native order replacement;
- optional Pacifica API agent-wallet signing, so the main wallet key does not
  need to be stored by the trading application.

Public market data requires no credentials. WalletAddress enables public
account queries and account WebSocket subscriptions. PrivateKey accepts the
base58-encoded 64-byte Solana keypair used for trading. When an API agent key
is used, configure the main account in WalletAddress and the derived public
key in AgentWallet.

Pacifica symbols are case-sensitive, so the connector preserves the exact
symbol spelling returned by the exchange. All wire payloads use concrete DTOs
without dynamic JSON trees, anonymous protocol objects, or protocol
dictionaries.

Official resources:

- [Pacifica API documentation](https://docs.pacifica.fi/api-documentation/api)
- [Pacifica REST API](https://docs.pacifica.fi/api-documentation/api/rest-api)
- [Pacifica WebSocket API](https://docs.pacifica.fi/api-documentation/api/websocket)
- [Pacifica signing guide](https://docs.pacifica.fi/api-documentation/api/signing)
- [Official Pacifica Python SDK](https://github.com/pacifica-fi/python-sdk)
- [StockSharp Pacifica connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/pacifica.html)

Pacifica and its marks are trademarks of their respective owner. StockSharp
is not affiliated with or endorsed by Pacifica.
