# StandX connector for StockSharp

The connector integrates StandX's official perpetual-futures REST and
WebSocket APIs. Public instrument discovery and market data work without
credentials. Portfolio data and trading use StandX wallet authentication; the
wallet private key signs the login challenge locally and is never sent to
StandX.

Supported functionality:

- perpetual-market discovery and current trading constraints;
- Level1, order-book snapshots, and public trades over WebSocket;
- recent public-trade snapshots and historical time-frame candles through REST;
- balances, positions, open orders, order history, and fills;
- locally signed market, limit, post-only, IOC, reduce-only, and attached
  take-profit/stop-loss orders;
- individual and filtered bulk cancellation;
- authenticated order, fill, balance, and position streams.

`Chain` selects BNB Smart Chain or Solana wallet authentication. `PrivateKey`
accepts an EVM hexadecimal private key for BSC or a base58-encoded 64-byte
Solana keypair. `WalletAddress` is optional; when supplied, it is checked
against the private key before authentication. Without a private key the
adapter stays in public market-data mode.

StandX provides live order, account, price, depth, and trade streams. Candles are retrieved from the official REST history endpoint and current candle updates are polled at the configured interval. Safe GET requests use bounded retry and backoff; trading writes are sent once over the signed order stream.

Official resources:

- [StandX API documentation](https://docs.standx.com/standx-api/standx-api)
- [Authentication](https://docs.standx.com/standx-api/perps-auth)
- [Perps HTTP API](https://docs.standx.com/standx-api/perps-http)
- [Perps WebSocket API](https://docs.standx.com/standx-api/perps-ws)
- [Rate limits](https://docs.standx.com/standx-api/rate-limits)
- [Official media assets](https://docs.standx.com/docs/resources/media-assets)
- [StockSharp StandX connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/standx.html)

StandX and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by StandX.
