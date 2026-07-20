# Orderly Network connector for StockSharp

The connector integrates Orderly Network perpetual markets through the current
official Omnichain REST and WebSocket APIs. Public reference data, order-book
snapshots, trades, and candle history work without a secret. Orderly requires a
registered account ID in the public WebSocket URL, so live market data requires
an account ID even when no private key is configured. Account data and trading
also require the base58-encoded ED25519 Orderly secret.

Every REST request, response, WebSocket command, event, keyed balance object,
and positional price level is represented by a concrete DTO. The protocol layer
contains no generic JSON trees, anonymous request bodies, protocol dictionaries,
or untyped JSON arrays.

## Supported functionality

- perpetual security lookup with official price, volume, minimum-quantity, and
  minimum-notional rules;
- public Level1 snapshots plus live ticker, best bid/ask, and trade changes;
- zero-auth Public Info API order-book snapshots followed by official
  `orderbookupdate` increments, timestamp validation, gap recovery, and reconnect
  snapshots;
- recent and live public trades;
- historical and live candles for all intervals shared by the public query API
  and WebSocket API;
- token holdings, perpetual positions, orders, and private fills;
- authenticated balance, position, and execution-report WebSocket streams;
- limit and market orders, GTC, IOC, FOK, post-only, reduce-only, visible
  quantity, and market-order slippage;
- native order amendment, cancellation by exchange or client order ID, and
  symbol-wide or account-wide cancellation;
- ED25519 request signing with exact compact request bodies and base64url
  signatures; trading writes are never retried automatically.

## Configuration

```csharp
var adapter = new OrderlyNetworkMessageAdapter(new IncrementalIdGenerator())
{
    AccountId = "0xYOUR_ORDERLY_ACCOUNT_ID",
    Secret = "YOUR_BASE58_ED25519_SECRET".ToSecureString(),
};
```

For REST-only public access, leave both values empty. For realtime public market
data, configure `AccountId`. Add `Secret` for account data and trading. The
production endpoints default to `https://api.orderly.org`,
`wss://ws-evm.orderly.org/ws/stream`, and
`wss://ws-private-evm.orderly.org/v2/ws/private/stream`; all remain configurable
for testnet or controlled routing.

## Official documentation

- [Orderly Omnichain API documentation](https://orderly.network/docs/build-on-omnichain/introduction)
- [API authentication](https://orderly.network/docs/build-on-omnichain/api-authentication)
- [Public Info API](https://orderly.network/docs/build-on-omnichain/public-info-api/introduction)
- [WebSocket API](https://orderly.network/docs/build-on-omnichain/websocket-api/introduction)
- [Create order](https://orderly.network/docs/build-on-omnichain/restful-api/private/create-order)
- [StockSharp Orderly Network connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/orderly_network.html)

Orderly Network and its marks are trademarks of their respective owner.
StockSharp is not affiliated with or endorsed by Orderly Network.
