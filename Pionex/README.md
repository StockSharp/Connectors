# Pionex connector for StockSharp

The connector integrates the current Pionex Trade API and invite-only Futures Beta API with their official public and authenticated WebSocket services.

## Supported functionality

- spot instruments on `BoardCodes.Pionex` (`PIONX`);
- perpetual futures on `BoardCodes.PionexFutures` (`PNXF`);
- security lookup, Level1, recent trades, order-book snapshots, and candles;
- official trade, depth, and futures index WebSocket streams;
- live candle updates aggregated from the official trade stream;
- spot limit, market, and IOC orders;
- futures limit, market-quantity, IOC, FOK, post-only, position-side, and
  reduce-only orders;
- order cancellation, security-scoped group cancellation, balances, isolated
  balances, futures positions, active orders, order history, and fills;
- authenticated WebSocket updates for balances, orders, fills, and futures
  positions;
- REST HMAC-SHA256 signing, spot query-authenticated WebSocket sessions,
  futures message authentication, heartbeat replies, reconnect, private-state
  hydration, bounded retry for reads, and subscription restoration.

Trading writes are never automatically retried. If a write fails after it may
have reached Pionex, inspect exchange state before submitting it again.

## Configuration

Public market data works without credentials. Set `Key` and `Secret` for
trading, portfolios, order history, and private streams. `Sections` selects
Spot, Futures, or both.

```csharp
var adapter = new PionexMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [PionexSections.Spot, PionexSections.Futures],
};
```

A spot market buy requires its quote-currency amount in
`PionexOrderCondition.QuoteAmount`; StockSharp order volume remains the
base-currency quantity for all other orders. Pionex order-history endpoints are
security-scoped. Supply `SecurityId` in an order-status request, or first trade
or query the symbols that the connector should track.

Default endpoints:

- REST: `https://api.pionex.com`;
- public WebSocket: `wss://ws.pionex.com/wsPub`;
- spot private WebSocket: `wss://ws.pionex.com/ws`;
- futures private WebSocket: `wss://ws.pionex.com/wsUA`.

Pionex marks the Futures Partner API as invite-only. Futures market data may be
public, but private futures access must be enabled by Pionex for the account.

## Official documentation

- [Pionex API documentation](https://www.pionex.com/docs/api-docs)
- [Trade REST API](https://www.pionex.com/docs/api-docs/trade-api)
- [Trade WebSocket API](https://www.pionex.com/docs/api-docs/trade-websocket)
- [Futures REST API](https://www.pionex.com/docs/api-docs/futures-api)
- [Futures WebSocket API](https://www.pionex.com/docs/api-docs/futures-websocket)
- [StockSharp Pionex connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/pionex.html)

Pionex and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Pionex.
