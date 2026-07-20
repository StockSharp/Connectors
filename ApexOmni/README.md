# ApeX Omni connector for StockSharp

The connector integrates the current ApeX Omni v3 REST and WebSocket APIs.
Public market data works without credentials. Portfolio data and trading use
an ApeX API key, API secret, and passphrase. Order submission additionally
requires the hexadecimal zkLink signing seed created during ApeX onboarding;
the seed is used locally and is never sent to ApeX.

Supported functionality:

- perpetual, pre-launch, prediction, and tokenized-stock discovery;
- Level1, order-book, public-trade, and candle snapshots and live streams;
- historical public trades and candles;
- account equity, wallets, positions, open orders, order history, and fills;
- locally signed limit, market, stop-loss, take-profit, post-only, IOC, FOK,
  and reduce-only orders;
- individual, symbol-wide, and account-wide cancellation;
- authenticated order, fill, wallet, and position updates;
- production and public-testnet environments.

Tokenized-stock instruments are available for discovery and public market data.
Trading them requires ApeX's separate RWA account, API credentials, and signing
context; the current adapter deliberately limits order submission to the primary
contract account.

The connector ships the official ApeX zkLink native signing libraries for
Windows x64, Linux x64/ARM64, and macOS x64/ARM64. Native ABI checksums are
validated before a signer is created. Public order-book deltas are applied only
after a snapshot and sequence gaps force a fresh subscription. Trading writes
are never retried automatically.

Every REST request, response, WebSocket command, event, positional price level,
and keyed candle payload is represented by a concrete DTO or streaming JSON
converter. The protocol layer contains no dynamic JSON trees, anonymous
protocol payloads, or protocol dictionaries.

Official resources:

- [ApeX Omni API documentation](https://api-docs.pro.apex.exchange/)
- [Official Python SDK](https://github.com/ApeX-Protocol/apexpro-openapi)
- [Official Node.js SDK](https://github.com/ApeX-Protocol/apexpro-connector-node)
- [ApeX Omni](https://omni.apex.exchange/)
- [StockSharp ApeX Omni connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/apex_omni.html)

ApeX Omni and its marks are trademarks of their respective owner. StockSharp
is not affiliated with or endorsed by ApeX Protocol.
