# StockSharp Injective connector

The connector integrates StockSharp with Injective spot and derivative
markets. It uses the current Exchange v2 transaction messages, REST
models, native Indexer gRPC streams and the Tendermint WebSocket interface.

## Supported operations

- spot, perpetual and expiry-futures market discovery;
- Level1 snapshots and live best quotes, trades and derivative oracle prices;
- REST order-book snapshots followed by native gRPC order-book streams;
- public trade history and live gRPC trade streams;
- historical candles and polling updates for active candles;
- bank and subaccount balances, derivative positions, orders and fills;
- live portfolio, position, order and account-fill streams;
- locally signed Exchange v2 market, limit, post-only, stop and take-profit
  orders, including reduce-only derivative orders;
- order cancellation, replacement and filtered group cancellation;
- mainnet and testnet endpoints.

Public market data requires no credentials. `WalletAddress` enables read-only
portfolio and order data. Trading additionally requires the matching
32-byte hexadecimal secp256k1 `PrivateKey`. The connector derives and validates
the `inj` address locally and never sends the private key to an API service.
`SubaccountIndex` selects a subaccount from zero through 255.

Transactions use Cosmos direct signing with Injective's
`ethsecp256k1.PubKey`, Keccak-256 and the current
`injective.exchange.v2` message types. Prices, quantities, trigger prices and
derivative margins are encoded in the v2 extended 18-decimal format. Gas and
fee settings are configurable because the connector does not simulate a
transaction before every order.

## Transport

The default mainnet services are:

- Indexer REST/gRPC-web:
  `https://sentry.exchange.grpc-web.injective.network`;
- native Indexer gRPC:
  `https://sentry.exchange.grpc.injective.network:443`;
- chain LCD: `https://sentry.lcd.injective.network`;
- chain WebSocket: `wss://sentry.tm.injective.network:443/websocket`.

The connector uses native gRPC server streams for exchange market and account
updates. The chain WebSocket tracks new block height and time; LCD provides the
initial block, account sequence, transaction broadcast and a fallback block
refresh. Streams reconnect automatically. Every endpoint can be overridden
independently for a compatible gateway or local infrastructure.

## Limitations

- The private key setting accepts a raw hexadecimal key; mnemonic, hardware
  wallet and delegated authz signing are not exposed by this connector.
- A market order needs a protection price. The connector derives it from the
  latest trade or top of book and applies `MarketOrderSlippage`.
- Balance symbols follow Indexer token metadata. An unknown denom is published
  under its chain denom without guessing a token symbol.
- Direct transactions use the configured gas and fee instead of automatic gas
  simulation.

## Documentation

- [Injective native developer documentation](https://docs.injective.network/developers-native)
- [Injective public endpoints](https://docs.injective.network/infra/public-endpoints)
- [Querying the Injective Indexer](https://docs.injective.network/developers-native/query-indexer)
- [Streaming the Injective Indexer](https://docs.injective.network/developers-native/query-indexer-stream)
- [Private-key transactions](https://docs.injective.network/developers-native/transactions/private-key)
- [Official TypeScript SDK](https://github.com/InjectiveLabs/injective-ts)
- [Official Python SDK](https://github.com/InjectiveLabs/sdk-python)
- [StockSharp Injective connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/injective.html)

Injective and its marks are trademarks of their respective owner. StockSharp
is not affiliated with or endorsed by Injective.
