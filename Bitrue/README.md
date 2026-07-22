# Bitrue connector for StockSharp

The connector integrates Bitrue spot and USDT-margined perpetual futures
through the exchange's official REST and WebSocket APIs. Public market data
works without credentials. Trading, balances, positions, orders, and fills
require an API key and secret.

The spot and futures APIs are separate protocols. The connector applies each API's native authentication, endpoints, symbols, listen keys, payloads, and stream channels.

## Supported functionality

- spot instruments on `BoardCodes.Bitrue` (`BTRUE`);
- USDT-margined perpetual contracts on `BoardCodes.BitrueFutures` (`BTRUF`);
- security lookup with price and volume steps, limits, and contract values;
- Level1, L2 order books, recent and live trades, and candles;
- official public WebSocket streams for spot depth and all futures live data;
- REST polling for spot ticker, trades, and candles, which the documented spot
  WebSocket API does not publish;
- separate spot and futures portfolios, balances, futures positions, orders,
  and fills;
- limit and market spot orders, and limit, market, IOC, FOK, and post-only
  futures orders;
- futures cross and isolated margin modes, leverage, and reduce-only orders;
- individual and filtered group cancellation;
- official private WebSocket order, fill, balance, and account updates;
- listen-key renewal, heartbeat, reconnect, and subscription restoration.

Bitrue's current futures V2 API exposes active-order lookup and fill history,
but no complete closed-order history endpoint. The connector reports the
active futures orders and historical fills the API makes available. The
deprecated futures `allOpenOrders` endpoint is not used; filtered group
cancellation fetches active orders and cancels them individually.

Trading writes are never retried automatically. If a write fails after it may
have reached the exchange, inspect exchange state before submitting it again.

## Configuration

```csharp
var adapter = new BitrueMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [BitrueSections.Spot, BitrueSections.Futures],
};
```

Default production endpoints:

- spot REST: `https://openapi.bitrue.com`;
- spot listen-key REST: `https://open.bitrue.com`;
- spot public WebSocket: `wss://ws.bitrue.com/market/ws`;
- spot private WebSocket: `wss://wsapi.bitrue.com`;
- futures REST: `https://fapi.bitrue.com`;
- futures listen-key REST: `https://fapiws-auth.bitrue.com`;
- futures public WebSocket:
  `wss://fmarket-ws.bitrue.com/kline-api/ws`;
- futures private WebSocket: `wss://fapiws.bitrue.com`.

Endpoint properties remain configurable for compatible Bitrue environments
and controlled routing.

## Official documentation

- [Bitrue spot API](https://github.com/Bitrue-exchange/Spot-official-api-docs)
- [Bitrue USDT-M futures V2 API](https://github.com/Bitrue-exchange/USDT-M-Future-open-api-docs/tree/main/v2)
- [StockSharp Bitrue connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/bitrue.html)

Bitrue and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Bitrue.
