# BTSE connector for StockSharp

The connector integrates BTSE spot, futures, and perpetual markets through the
current official Spot v3.3 and Futures v2.3 REST APIs and both BTSE WebSocket
families. Public reference and market-data operations do not require
credentials. Portfolio data, private streams, and trading require a BTSE API
key with the corresponding permissions.

Every REST request, response, WebSocket command, event, positional candle, and
positional price level is represented by a concrete DTO. The protocol layer
contains no dynamic JSON trees, anonymous request bodies, protocol
dictionaries, or untyped arrays.

## Supported functionality

- spot instruments on `BoardCodes.Btse` (`BTSE`) and futures/perpetual
  instruments on `BoardCodes.BtseFutures` (`BTSEF`);
- security lookup with price, size, minimum-order, maximum-order, contract, and
  expiry metadata;
- REST Level1 snapshots plus official `snapshotL1` best bid/ask updates;
- REST L2 snapshots and official `update:<symbol>_0` incremental books with
  sequence validation, zero-size deletion, automatic gap resubscription, and
  reconnect restoration;
- recent, historical, and live public trades using the current spot
  `tradeHistoryApi` and futures `tradeHistoryApiV3` topics;
- historical candles for every interval supported by both current REST APIs;
- spot wallet balances, futures wallet assets, and futures positions;
- active orders and private trade history;
- current private order topics (`notificationApiV3` for spot and
  `notificationApiV4` for futures), `fills`/`fillsV2`, and `allPositionV4`;
- limit and market orders, GTC, IOC, FOK, post-only, reduce-only, and stop
  triggers;
- atomic price/size replacement plus individual, symbol-wide, side-filtered,
  and account-wide cancellation;
- authenticated reconnect and restoration of public and private subscriptions.

BTSE provides candle history through REST but does not document a candle
WebSocket topic in these API versions, so the connector correctly reports
historical candles only. Spot balances and futures wallet balances are REST
snapshots; futures positions continue in realtime through `allPositionV4`.
Trading writes are never retried automatically. If a write may have reached
the exchange, inspect exchange state before submitting it again.

BTSE also offers FIX connectivity as a separately onboarded product. This
connector deliberately uses the generally available REST and WebSocket APIs;
it does not imitate or tunnel an institutional FIX session.

## Configuration

```csharp
var adapter = new BTSEMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [BTSESections.Spot, BTSESections.Futures],
};
```

The production REST, general WebSocket, and order-book WebSocket addresses are
configurable independently for each section. Replace all three addresses of a
section together when routing it to BTSE testnet.

## Official documentation

- [BTSE API documentation](https://btsecom.github.io/docs/)
- [Spot API v3.3](https://btsecom.github.io/docs/spotV3_3/en/)
- [Futures API v2.3](https://btsecom.github.io/docs/futuresV2_3/en/)
- [Wallet API](https://btsecom.github.io/docs/wallet/en/)
- [StockSharp BTSE connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/btse.html)

BTSE and its marks are trademarks of their respective owner. StockSharp is not
affiliated with or endorsed by BTSE.
