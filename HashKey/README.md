# HashKey Global connector for StockSharp

The connector integrates the current HashKey Global API for spot crypto and
perpetual futures. Public reference and market data work without credentials.
Account, order, fill, balance, and position access requires an API key and
secret with the corresponding read or trade permissions.

Every request, response, WebSocket subscription, event envelope, order-book
level, candle, balance, order, fill, and position is represented by a concrete
DTO. Array-shaped price levels and candles use dedicated typed JSON converters.
The protocol layer contains no dynamic JSON trees, anonymous protocol bodies,
protocol dictionaries, or untyped protocol arrays.

## Supported functionality

- spot instruments on `BoardCodes.HashKey` (`HASHKEY`) and perpetual contracts
  on `BoardCodes.HashKeyFutures` (`HASHKEYF`), including trading state, base and
  quote assets, price and quantity steps, quantity limits, multiplier, and
  underlying asset;
- REST Level1 snapshots and realtime WebSocket v2 BBO and 24-hour ticker
  updates;
- REST L2 snapshots up to 200 levels and 100 ms WebSocket v2 depth snapshots;
- recent public trades followed by realtime WebSocket v2 trades;
- paged historical OHLCV retrieval and realtime candle updates for the official
  1m, 3m, 5m, 15m, 30m, 1h, 2h, 4h, 6h, 8h, 12h, 1d, 1w, and 1M intervals;
- spot balances, futures balances, long and short positions, leverage,
  liquidation prices, and unrealized PnL;
- spot and futures open/history order snapshots and private fills;
- spot market, limit, and maker-only orders;
- futures limit, market, and stop orders, explicit open/close direction,
  GTC/IOC/FOK/maker-only mapping, self-trade prevention, individual
  cancellation, and bulk cancellation;
- the private user-data WebSocket for order, fill, balance, and futures-position
  updates, with automatic heartbeat responses and `listenKey` renewal;
- documented query and order rate limits, server-time synchronization, and
  retries for safe reads only. Trading writes are never retried automatically.

HashKey represents a futures market order as a `LIMIT` request whose
`priceType` is `MARKET`; the connector performs that protocol mapping without
presenting it as a limit order to StockSharp. Futures quantities are validated
as integer contract counts. HashKey public depth v2 messages contain complete
books, so the connector emits snapshot-complete books and does not advertise
incremental order-book support.

## Configuration

```csharp
var adapter = new HashKeyMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [HashKeySections.Spot, HashKeySections.Futures],
};
```

Set `IsDemo = true` to use the official HashKey Global sandbox REST and
WebSocket hosts. All three endpoints remain configurable for controlled routing.

## Official documentation

- [HashKey Global API reference](https://docs.hashkey.com/glb/en/index.html)
- [HashKey Global production site](https://global.hashkey.com/)
- [StockSharp HashKey Global connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/hashkey_global.html)

HashKey and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by HashKey.
