# WEEX connector for StockSharp

The connector integrates the current WEEX V3 Spot and USDT perpetual Futures
REST APIs with the official public and authenticated WebSocket services. Every
wire request, response, subscription, and event is represented by a concrete
DTO; the implementation does not use dynamic JSON trees, protocol dictionaries,
anonymous wire bodies, or untyped arrays.

## Supported functionality

- spot instruments on `BoardCodes.Weex` (`WEEX`);
- USDT perpetual futures on `BoardCodes.WeexFutures` (`WEEXF`);
- security lookup, Level1, recent trades, order books, and candles;
- official V3 WebSocket ticker, trade, depth, and candle streams;
- incremental depth with sequence-gap detection and automatic resubscription;
- spot and futures limit and market orders;
- futures stop-loss and take-profit market or limit orders;
- GTC, IOC, FOK, post-only, hedge-mode position side, attached TP/SL, order
  cancellation, group cancellation, and futures position close;
- spot balances, futures balances and positions, active orders, order history,
  conditional orders, and fills;
- authenticated V3 WebSocket updates for accounts, orders, fills, and positions;
- HMAC-SHA256 signing, server-time synchronization, heartbeat handling, bounded
  retry for safe reads, reconnect, fresh private authentication, state hydration,
  and subscription restoration.

Trading writes are never automatically retried. If a write fails after it may
have reached WEEX, inspect the order state before submitting another request.

WEEX Spot V3 currently returns a recent candle window and does not reliably
honour historical range parameters. The connector filters that response locally,
but cannot synthesize older spot history that the official endpoint did not
return. Futures historical candles use the documented paged endpoint.

## Configuration

Public market data works without credentials in market-data-only mode. Set
`Key`, `Secret`, and `Passphrase` for trading, portfolios, and private streams.
`Sections` selects Spot, Futures, or both.

```csharp
var adapter = new WeexMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Passphrase = "YOUR_API_PASSPHRASE".ToSecureString(),
    Sections = [WeexSections.Spot, WeexSections.Futures],
};
```

Default endpoints:

- Spot REST: `https://api-spot.weex.com`;
- Futures REST: `https://api-contract.weex.com`;
- Spot public/private WebSocket: `wss://ws-spot.weex.com/v3/ws/public` and
  `wss://ws-spot.weex.com/v3/ws/private`;
- Futures public/private WebSocket: `wss://ws-contract.weex.com/v3/ws/public`
  and `wss://ws-contract.weex.com/v3/ws/private`.

Private WebSocket authentication is performed in the HTTP upgrade headers and
is regenerated for every reconnect. WEEX also requires a `User-Agent` header on
public WebSocket connections; the connector supplies it automatically.

## Official documentation

- [WEEX API documentation](https://www.weex.com/api-doc/)
- [WEEX Spot V3 API](https://www.weex.com/api-doc/spot/)
- [WEEX Futures V3 API](https://www.weex.com/api-doc/contract/)
- [WEEX official media kit](https://www.weex.com/Media-kit)
- [StockSharp WEEX connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/weex.html)

WEEX and its marks are trademarks of their respective owner. StockSharp is not
affiliated with or endorsed by WEEX.
