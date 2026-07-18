# Phemex connector for StockSharp

The connector integrates Phemex spot and current linear perpetual markets through
the official Open API. REST is used for reference data, snapshots, history,
portfolio state, and trading. The official WebSocket service supplies public
market data and authenticated account, order, fill, and position updates.

Every request, response, subscription, event, and positional array is represented
by a concrete DTO. The protocol implementation does not use dynamic JSON trees,
protocol dictionaries, anonymous request bodies, or untyped arrays.

## Supported functionality

- spot instruments on `BoardCodes.Phemex` (`PHMEX`);
- linear perpetual instruments on `BoardCodes.PhemexFutures` (`PHMXF`);
- security lookup, Level1, order books, recent trades, and historical candles;
- official trade, order-book, and 24-hour ticker WebSocket streams;
- live candle updates aggregated from the official trade stream;
- spot and perpetual limit and market orders;
- GTC, IOC, FOK, and post-only execution policies;
- quote-amount spot market buys;
- perpetual position side and reduce-only instructions;
- order replacement, individual cancellation, and security-scoped group cancellation;
- balances, perpetual positions, active orders, order history, and fills;
- authenticated WebSocket updates for balances, orders, fills, and positions;
- HMAC-SHA256 authentication, heartbeat, reconnect, order-book reconstruction,
  and subscription restoration.

The first version covers spot products and the current linear perpetual V2
products marked `Normal` by Phemex. It intentionally excludes legacy inverse
contracts. Order-history endpoints are security-scoped: supply `SecurityId` in
an order-status request, or first submit/query the symbols that the connector
should track.

Trading writes are never retried automatically. If a write fails after it may
have reached Phemex, inspect exchange state before submitting it again.

## Configuration

Public market data works without credentials. Set `Key` and `Secret` for
trading, portfolios, order history, and private streams. `Sections` selects
Spot, Futures, or both.

```csharp
var adapter = new PhemexMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [PhemexSections.Spot, PhemexSections.Futures],
};
```

A spot market buy requires its quote-currency amount in
`PhemexOrderCondition.QuoteAmount`; StockSharp order volume remains the
base-currency quantity for other orders.

Default endpoints:

- REST: `https://api.phemex.com`;
- public and authenticated WebSocket: `wss://ws.phemex.com`.

## Official documentation

- [Phemex Open API documentation](https://phemex-docs.github.io/)
- [Phemex API documentation source](https://github.com/phemex/phemex-api-docs)
- [StockSharp Phemex connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/phemex.html)

Phemex and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Phemex.
