# WOO X connector for StockSharp

The connector integrates WOO X spot and perpetual markets through the current
official REST and WebSocket V2 APIs. Public instruments, snapshots, recent and
historical market data work without API credentials. Private account data and
trading require an API key, API secret, and the application ID shown in the
WOO X console.

All REST requests and responses, WebSocket commands, events, keyed balance
objects, keyed position objects, and positional price levels are converted to
concrete DTOs. The protocol layer contains no dynamic JSON trees, anonymous
request bodies, protocol dictionaries, or untyped arrays.

## Supported functionality

- spot instruments on `BoardCodes.WooX` (`WOOX`) and perpetual instruments on
  `BoardCodes.WooXFutures` (`WOOXF`);
- security lookup with price and volume increments and order limits;
- Level1 snapshots and the official live ticker stream;
- L2 REST snapshots and official 100-level WebSocket snapshots;
- recent, historical, and live public trades;
- historical and live candles for every interval common to REST and WebSocket;
- balances, account positions, active and historical orders, and executions;
- authenticated `balance`, `position`, and `executionreport` WebSocket topics;
- limit, market, IOC, FOK, and post-only orders;
- quote-currency market-order amounts, cross and isolated margin, one-way and
  hedge position sides, leverage, and reduce-only orders;
- order amendment, individual cancellation, symbol cancellation, and global
  pending-order cancellation;
- heartbeat, reconnect, authentication, and subscription restoration.

Trading writes are never retried automatically. If a write fails after it may
have reached WOO X, inspect exchange state before submitting it again.

## Configuration

```csharp
var adapter = new WooXMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    ApplicationId = "YOUR_APPLICATION_ID",
};
```

Public REST and market-data streaming do not require API credentials. If
`ApplicationId` is empty for a public-only connection, the connector creates a
connection-scoped identifier for the public WebSocket path. An actual WOO X
application ID is mandatory whenever credentials are configured.

Default production endpoints:

- REST: `https://api.woox.io`;
- historical REST: `https://api-pub.woox.io`;
- public WebSocket: `wss://wss.woox.io/ws/stream/{application_id}`;
- private WebSocket:
  `wss://wss.woox.io/v2/ws/private/stream/{application_id}`.

Endpoint properties remain configurable for official staging environments and
controlled routing.

## Official documentation

- [WOO X API reference](https://docs.woox.io/products/woo-x)
- [WOO X authentication](https://docs.woox.io/products/woo-x#authentication)
- [WOO X WebSocket API V2](https://docs.woox.io/products/woo-x#websocket-api-v2)
- [StockSharp WOO X connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/woox.html)

WOO X and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by WOO X.
