# XT.COM connector for StockSharp

The connector integrates the current XT.COM Spot and USDT-M Futures Open APIs, including their official public and authenticated WebSocket services. Spot and futures use separate REST hosts, signing rules, stream commands, and private session tokens; the implementation keeps those protocols isolated.

## Supported functionality

- spot instruments on `BoardCodes.Xt` (`XTCOM`);
- USDT-M perpetual futures on `BoardCodes.XtFutures` (`XTCMF`);
- security lookup with price, quantity, minimum-volume, and contract-size
  metadata;
- Level1 snapshots, recent trades, order-book snapshots, and historical
  candles;
- official real-time trade, limited-depth, and futures aggregate-ticker
  WebSocket streams;
- live candles aggregated from the official real-time trade stream;
- spot and futures limit and market orders with GTC, IOC, FOK, and GTX
  (post-only) policies;
- order cancellation, security-scoped group cancellation, balances, futures
  positions, active orders, order history, and fills;
- authenticated balance, order, fill, and futures-position WebSocket updates;
- the documented Spot HMAC-SHA256 header signature and Futures HMAC-SHA256
  signature, text `ping`/`pong` heartbeat, reconnect, subscription restoration,
  and private token renewal;
- bounded retries for read-only REST calls. Trading writes are never retried.

If a write fails after it may have reached XT.COM, inspect exchange state before
submitting it again.

## Configuration

Public market data works without credentials. Set `Key` and `Secret` for
trading, portfolios, order history, and private streams. The API key must have
the required XT.COM permissions and IP restrictions configured in the exchange
account. `Sections` selects Spot, Futures, or both.

```csharp
var adapter = new XtMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [XtSections.Spot, XtSections.Futures],
};
```

A spot market buy requires its quote-currency amount in
`XtOrderCondition.QuoteAmount`; other spot orders use StockSharp order volume as
the base-currency quantity. Futures volume is an integer number of contracts,
and `XtOrderCondition.PositionSide` must explicitly be `Long` or `Short`.
XT.COM's regular futures order endpoint does not provide an atomic reduce-only
flag, so close-only orders are rejected instead of being silently weakened.

Default endpoints:

- Spot REST: `https://sapi.xt.com`;
- Futures REST: `https://fapi.xt.com`;
- Spot public WebSocket: `wss://stream.xt.com/public`;
- Futures public WebSocket: `wss://fstream.xt.com/ws/market`;
- Spot private WebSocket: `wss://stream.xt.com/private`;
- Futures private WebSocket: `wss://fstream.xt.com/ws/user`.

## Official documentation

- [XT.COM API portal](https://doc.xt.com/)
- [Spot API access and signing](https://doc.xt.com/docs/spot/Access%20Description/SignatureGeneration)
- [Spot public WebSocket](https://doc.xt.com/docs/spot/WebSocket%20Public/wss-general)
- [Spot private WebSocket](https://doc.xt.com/docs/spot/WebSocket%20Private/GeneralWSSInfo)
- [Futures API access and signing](https://doc.xt.com/docs/futures/Access%20Description/SignatureGeneration)
- [Futures public WebSocket](https://doc.xt.com/docs/futures/WebsocKetV2/General_WSS_information)
- [Futures private WebSocket](https://doc.xt.com/docs/futures/UserWebsocket/General_WSS_information)
- [StockSharp XT.COM connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/xtcom.html)

XT.COM and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by XT.COM.
