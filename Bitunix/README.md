# Bitunix connector for StockSharp

The connector integrates Bitunix spot and USDT-margined perpetual futures
through the exchange's official OpenAPI. Public market data works without
credentials; portfolios, orders, fills, positions, and trading require an API
key and secret.

All REST requests, responses, WebSocket commands, and WebSocket events use
concrete DTOs. The protocol layer contains no dynamic JSON trees, anonymous
request bodies, or untyped protocol collections.

## Supported functionality

- spot instruments on `BoardCodes.Bitunix` (`BTUNX`);
- perpetual futures on `BoardCodes.BitunixFutures` (`BTUXF`);
- security lookup with price step, volume step, minimum volume, and futures
  leverage metadata;
- spot Level1, L2 order books, and candles through typed REST polling;
- futures Level1, L2 order books, live trades, and candles through REST and the
  official public WebSocket;
- spot balances and futures balances and positions;
- active and historical orders and fills;
- spot limit and market orders;
- futures limit, market, post-only, IOC, and FOK orders, with cross or isolated
  margin and configurable leverage;
- individual and group cancellation;
- atomic futures order replacement;
- private futures balance, position, and order WebSocket channels;
- heartbeat, reconnect, and subscription restoration.

Bitunix's spot WebSocket API is an authenticated request/response RPC API, not
a server-push market stream. The connector therefore polls the official spot
REST endpoints at `PollingInterval`. The official spot OpenAPI does not expose
public time-and-sales, so spot trade subscriptions are reported as unsupported;
futures trades use the official `trade` WebSocket channel.

Trading writes are never retried automatically. If a write fails after it may
have reached the exchange, inspect exchange state before submitting it again.

## Configuration

```csharp
var adapter = new BitunixMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [BitunixSections.Spot, BitunixSections.Futures],
    PollingInterval = TimeSpan.FromSeconds(1),
};
```

Default endpoints:

- spot REST: `https://openapi.bitunix.com`;
- futures REST: `https://fapi.bitunix.com`;
- futures public WebSocket: `wss://fapi.bitunix.com/public/`;
- futures private WebSocket: `wss://fapi.bitunix.com/private/`.

The futures WebSocket accepts at most five client messages per second and 300
channel subscriptions per connection. The connector rate-limits outbound
commands and restores subscriptions after reconnect. Spot order lookup requires
a trading pair because that is required by the official API; symbols used by
the current connector session are tracked automatically.

## Official documentation

- [Bitunix Futures OpenAPI](https://www.bitunix.com/api-docs/futures/common/introduction.html)
- [Bitunix Spot OpenAPI](https://www.bitunix.com/api-docs/spots/en_us/)
- [Bitunix WebSocket API](https://www.bitunix.com/api-docs/futures/websocket/prepare/WebSocket.html)
- [Official Bitunix OpenAPI examples](https://github.com/BitunixOfficial/open-api)
- [StockSharp Bitunix connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/bitunix.html)

Bitunix and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Bitunix.
