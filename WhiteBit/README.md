# WhiteBIT connector for StockSharp

The connector integrates the current WhiteBIT REST API with the official public
and authenticated WebSocket streams. It exposes spot, collateral spot, perpetual
futures, and region-available TradFi futures through the StockSharp message model.

Every wire request and response is represented by a concrete DTO. Positional
WebSocket payloads and JSON objects keyed by market or asset are decoded by typed
streaming converters; the implementation does not use dynamic JSON trees,
protocol dictionaries, anonymous wire bodies, or untyped arrays.

## Supported functionality

- spot instruments on `BoardCodes.WhiteBit` (`WBIT`);
- collateral spot instruments on `BoardCodes.WhiteBitMargin` (`WBIM`);
- perpetual and available TradFi futures on `BoardCodes.WhiteBitFutures` (`WBIF`);
- security lookup, Level1, recent trades, incremental order books, and candles;
- official WebSocket market statistics, trades, depth, and candle updates;
- REST candle history with paging inside WhiteBIT's 1440-record request limit;
- limit, market, stop-limit, and stop-market orders;
- order modification, cancellation, filtered group cancellation, and position close;
- post-only, IOC, reduce-only, and hedge-mode position-side parameters;
- spot and collateral balances, positions, active orders, order history, and fills;
- authenticated WebSocket updates for balances, orders, executions, deals, and
  collateral positions;
- HMAC-SHA512 signing, monotonic nonces, keepalive, bounded retry for safe reads,
  reconnect, reauthorization, state hydration, and subscription restoration.

Trading writes are never automatically retried. If a write fails after it may
have reached WhiteBIT, inspect the order state before submitting another request.

WhiteBIT candle updates do not identify their interval. The connector therefore
uses one official WebSocket connection per distinct live market/interval pair so
simultaneous candle subscriptions cannot be routed to the wrong StockSharp series.

## Configuration

Public market data works without credentials when the adapter is used in
market-data-only mode. Set `Key` and `Secret` for trading, portfolios, and private
streams. `Sections` selects spot, margin, futures, or any combination.

```csharp
var adapter = new WhiteBitMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [
        WhiteBitSections.Spot,
        WhiteBitSections.Margin,
        WhiteBitSections.Futures,
    ],
};
```

Default endpoints:

- REST: `https://whitebit.com`;
- WebSocket: `wss://api.whitebit.com/ws`.

Set `RestEndpoint` to `https://whitebit.eu` when the account belongs to the EU
platform. WhiteBIT does not publish a separate API sandbox. Its documentation
recommends demo assets such as DBTC and DUSDT for practice where available.

The API key needs the permissions required by the selected operations. Private
WebSocket authorization uses a short-lived token obtained from
`POST /api/v4/profile/websocket_token`; a fresh token is requested after every
reconnect.

## Official documentation

- [WhiteBIT API documentation](https://docs.whitebit.com/)
- [REST authentication](https://docs.whitebit.com/api-reference/authentication)
- [WebSocket authentication](https://docs.whitebit.com/websocket/authentication)
- [WhiteBIT corporate identity and official logo archive](https://whitebit.com/brand-guideline)
- [StockSharp WhiteBIT connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/whitebit.html)

WhiteBIT and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by WhiteBIT.
