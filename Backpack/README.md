# Backpack Exchange connector for StockSharp

The connector integrates Backpack Exchange spot and perpetual markets through
the current official REST API and unified WebSocket endpoint. Public reference
and market-data operations do not require credentials. Account data, private
streams, and trading require an ED25519 API key pair created in Backpack.

## Supported functionality

- crypto spot and tokenized-equity instruments on `BoardCodes.Backpack`
  (`BACKP`), and perpetual instruments on `BoardCodes.BackpackFutures`
  (`BACKPF`);
- security lookup with price, volume, and minimum-order increments;
- Level1 REST snapshots plus live ticker, best bid/ask, index, and mark-price
  updates;
- L2 REST snapshots followed by official 200 ms incremental depth, including
  update-sequence validation, gap recovery, reconnect snapshots, and subscription
  restoration;
- recent, historical, and live public trades;
- historical and live candles for all intervals shared by the REST and
  WebSocket APIs;
- balances, perpetual positions, active and historical orders, and fills;
- authenticated order and position WebSocket streams;
- limit and market orders, GTC, IOC, FOK, post-only, reduce-only, and
  quote-currency market-order quantities;
- individual, symbol-wide, side-filtered, and account-wide cancellation;
- authenticated reconnect and restoration of public and private subscriptions.

Backpack does not expose a balance WebSocket stream, so balance subscriptions
receive a REST snapshot while position changes continue in realtime. Backpack
also does not expose an order-replacement endpoint; cancel and submit a new
order instead. Trading writes are never retried automatically. If a write may
have reached the exchange, inspect exchange state before submitting it again.

## Configuration

```csharp
var adapter = new BackpackMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_ED25519_PUBLIC_KEY".ToSecureString(),
    Secret = "YOUR_ED25519_PRIVATE_KEY".ToSecureString(),
};
```

The private key must be the Base64-encoded ED25519 seed issued by Backpack.
Default production endpoints are `https://api.backpack.exchange` and
`wss://ws.backpack.exchange`. Both endpoint properties remain configurable for
controlled routing.

## Official documentation

- [Backpack Exchange API documentation](https://docs.backpack.exchange/)
- [REST API reference](https://docs.backpack.exchange/#tag/Markets)
- [WebSocket streams](https://docs.backpack.exchange/#tag/Streams)
- [StockSharp Backpack connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/backpack.html)

Backpack Exchange and its marks are trademarks of their respective owner.
StockSharp is not affiliated with or endorsed by Backpack Exchange.
