# GMTrade connector for StockSharp

The connector integrates the official GMTrade production data services for
the GMX-Solana perpetual protocol. It provides live oracle market data,
historical and streaming candles, indexed trades, and read-only Solana wallet
account monitoring.

Supported functionality:

- discovery of every enabled perpetual market and its collateral pool;
- live Level1 oracle bounds over the official GraphQL WebSocket service;
- historical trades from the official Subsquid indexer and continuous polling
  for new indexed executions;
- historical and live 1-minute, 5-minute, 15-minute, 1-hour, 2-hour, 4-hour,
  daily, weekly, and 30-day candles;
- wallet SOL and known SPL-token balances through Solana JSON-RPC;
- current perpetual positions and open orders, with live GraphQL updates;
- indexed wallet fills through the order-status subscription.

GMTrade is an AMM-based perpetual venue and does not expose a central-limit
order book. Level1 bid and ask values are the protocol's signed oracle
minimum and maximum bounds, not resting quotes. Public trades are delivered
by the official indexer because GMTrade does not publish a public trade
WebSocket stream.

Order construction is intentionally not advertised by this connector. The
production keeper accepts already signed Solana transaction groups, while
the complete order builder is provided by GMTrade's Rust and JavaScript SDKs.
The connector never forwards a private key and does not pretend that the
keeper GraphQL mutation is an order-entry API.

`WalletAddress` is optional. With no wallet configured, all public market data remains available.

Official resources:

- [GMTrade documentation](https://docs.gmtrade.xyz/)
- [GMTrade application](https://gmtrade.xyz/)
- [Official GMX-Solana SDK and programs](https://github.com/gmsol-labs/gmx-solana)
- [Trading documentation](https://docs.gmtrade.xyz/about/trading)
- [StockSharp GMTrade connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/gmtrade.html)

GMTrade and its marks are trademarks of their respective owner. StockSharp
is not affiliated with or endorsed by GMTrade.
