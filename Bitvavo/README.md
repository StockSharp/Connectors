# Bitvavo connector for StockSharp

The connector integrates Bitvavo European crypto spot markets through the
current Exchange REST API v2 and Exchange WebSocket API v2. Public reference
and market-data operations do not require credentials. Account data, private
streams, and trading require an API key with the corresponding Bitvavo
permissions.

Every REST request, response, WebSocket command, event, price level, and candle
is represented by a concrete DTO. The protocol layer contains no dynamic JSON
trees, anonymous request bodies, protocol dictionaries, or untyped arrays.

## Supported functionality

- crypto spot instruments on `BoardCodes.Bitvavo` (`BITVAVO`), including
  trading status, tick size, quantity precision, and order limits;
- Level1 REST snapshots plus live best bid/ask, last trade, and 24-hour OHLCV;
- L2 REST snapshots followed by official incremental book updates, with strict
  `nonce` validation, buffering, gap recovery, reconnect snapshots, and
  subscription restoration;
- recent and historical public trades, split into the API's 24-hour windows,
  plus the live trades channel;
- historical candles for every REST interval from one minute through one month,
  and live candles for every WebSocket interval from one minute through one day;
- account balances, open orders, market-filtered historical orders, and fills;
- authenticated account WebSocket events for realtime order states and fills;
- market, limit, stop-loss, stop-limit, take-profit, and take-profit-limit
  orders, GTC, IOC, FOK, post-only, native update, individual cancellation, and
  market-wide or account-wide cancellation;
- the mandatory Bitvavo `operatorId` on every create, update, and cancel request;
- authenticated reconnect and restoration of public and private subscriptions.

Bitvavo does not expose a balance WebSocket channel. A portfolio subscription
therefore receives a REST snapshot and is refreshed after each private fill.
The Exchange API requires a market for historical order and fill requests;
all-market order-status requests return all open orders and then continue with
realtime account events. Trading writes are never retried automatically. If a
write may have reached the exchange, inspect exchange state before submitting
it again.

## Configuration

```csharp
var adapter = new BitvavoMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    OperatorId = 1,
};
```

Use a stable positive `OperatorId` for each trader or trading algorithm. The
default production endpoints are `https://api.bitvavo.com/v2` and
`wss://ws.bitvavo.com/v2`. Both endpoint properties remain configurable for
controlled routing.

## Official documentation

- [Bitvavo API documentation](https://docs.bitvavo.com/)
- [Exchange REST API v2](https://docs.bitvavo.com/docs/rest-api/)
- [Exchange WebSocket API v2](https://docs.bitvavo.com/docs/websocket-api/)
- [Local order book synchronization](https://docs.bitvavo.com/docs/manage-order-book/)
- [StockSharp Bitvavo connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/bitvavo.html)

Bitvavo and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Bitvavo.
