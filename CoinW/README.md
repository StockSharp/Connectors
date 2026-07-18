# CoinW connector for StockSharp

The connector integrates CoinW Spot and perpetual Futures REST APIs with the
official public and authenticated WebSocket services. Every protocol request,
response, command, and event is represented by a concrete DTO; the connector
does not use dynamic JSON trees, protocol dictionaries, anonymous wire bodies,
or untyped arrays.

## Supported functionality

- spot instruments on `BoardCodes.CoinW` (`COINW`);
- perpetual futures on `BoardCodes.CoinWFutures` (`CNWF`);
- security lookup, Level1, recent trades, order books, and candles;
- official ticker, trade, depth, and candle WebSocket streams;
- spot incremental-depth sequence validation and automatic resubscription;
- spot and futures limit and market orders;
- futures trigger orders, attached stop-loss/take-profit, isolated/cross margin,
  leverage, and all three documented quantity units;
- dedicated futures position closing (CoinW does not close a hedged position by
  submitting an ordinary opposite-side order);
- order cancellation, security-scoped group cancellation, balances, positions,
  active orders, order history, and spot fills;
- authenticated WebSocket updates for balances, orders, futures positions, and
  futures fills;
- MD5 spot signing, HMAC-SHA256 futures signing, heartbeats, reconnect,
  re-authentication, state hydration, bounded retry for safe reads, and
  subscription restoration.

Trading writes are never automatically retried. If a write fails after it may
have reached CoinW, inspect exchange state before submitting another request.

## Configuration

Public market data works without credentials. Set `Key` and `Secret` for
trading, portfolios, order history, and private streams. `Sections` selects
Spot, Futures, or both.

```csharp
var adapter = new CoinWMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [CoinWSections.Spot, CoinWSections.Futures],
};
```

Default endpoints:

- Spot and Futures REST: `https://api.coinw.com`;
- Spot public/private WebSocket: `wss://ws.futurescw.com`;
- Futures public/private WebSocket: `wss://ws.futurescw.com/perpum`.

CoinW Spot WebSocket offers both a Socket.IO/token method and a direct standard
WebSocket method. The connector deliberately uses the documented direct method,
which needs no public token and fits StockSharp's reconnect model.

## Official documentation

- [CoinW API documentation](https://www.coinw.com/api-doc/en/common/introduction)
- [CoinW Spot Trading API](https://www.coinw.com/api-doc/en/spot-trading)
- [CoinW Futures Trading API](https://www.coinw.com/api-doc/en/futures-trading)
- [StockSharp CoinW connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/coinw.html)

CoinW and its marks are trademarks of their respective owner. StockSharp is not
affiliated with or endorsed by CoinW.
