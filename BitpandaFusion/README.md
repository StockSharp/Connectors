# Bitpanda Fusion connector for StockSharp

The connector integrates the current Bitpanda Fusion REST API for European
crypto liquidity aggregation and trading. A Fusion API key is required for
every endpoint, including reference and market data. The key needs the `Read`
scope for market data, balances, account information, orders, and trade history,
and the `Trade` scope for order entry and cancellation.

## Supported functionality

- active Fusion pairs on `BoardCodes.BitpandaFusion` (`BPFUSION`), including
  base and quote assets, price step, quantity step, maximum order size, currency,
  and asset names;
- current midpoint and 24-hour high, low, and volume snapshots;
- aggregated L2 order-book snapshots with a configurable depth from 1 to 100;
- historical OHLCV candles for the official 1m, 5m, 10m, 15m, 30m, 1h, 4h,
  and 1d intervals, up to the documented 1,440 bars per request;
- account balance snapshots with available and locked quantities;
- cursor-paginated order and private-trade history, including exact order lookup;
- market, limit, stop-market, and stop-limit orders, GTC/IOC/FOK/GTD mapping,
  expiry time, and individual cancellation;
- conservative global, market-data, and order-entry rate limiting; safe GET
  requests retry transient failures, while trading writes are never retried.

The ticker `price` field is explicitly documented by Bitpanda as a midpoint.
StockSharp has no separate Level1 midpoint field, so the connector maps it to
`LastTradePrice` and does not claim that it is an exchange trade. For aggregated
depth, `totalQuantity` is used when present and `quantity` is the compatibility
fallback. The ticker response has no observation timestamp, so its Level1
snapshot is stamped with the connector receipt time in UTC.

## REST-only behavior

The current public Fusion developer platform, updated on 14 July 2026, exposes
the Fusion API through REST (HTTP). It does not publish a customer WebSocket or
FIX contract. Consequently this connector deliberately provides finite REST
snapshots and history subscriptions and marks them finished; it does not emulate
realtime delivery by polling. Public trade ticks are also not advertised because
the current API has no public recent-trades endpoint.

## Configuration

```csharp
var adapter = new BitpandaFusionMessageAdapter(new IncrementalIdGenerator())
{
    Token = "YOUR_FUSION_API_KEY".ToSecureString(),
};
```

The production endpoint is `https://api.fusion.bitpanda.com/`. `Address` remains
configurable for controlled routing and must be an absolute HTTPS URI.

## Official documentation

- [Fusion Developer Platform](https://docs.fusion.bitpanda.com/)
- [Fusion API getting started](https://docs.fusion.bitpanda.com/getting-started-370709m0)
- [Fusion rate limits](https://docs.fusion.bitpanda.com/rate-limits-370893m0)
- [Official Fusion CLI](https://github.com/bitpanda-labs/bitpanda-fusion-cli)
- [StockSharp Bitpanda Fusion connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/bitpanda_fusion.html)

Bitpanda and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Bitpanda.
