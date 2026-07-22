# Gemini connector for StockSharp

The connector integrates Gemini spot and perpetual markets through the current
REST API and the production Trading WebSocket API. Public reference and market
data do not require credentials. Private REST snapshots require an API key and
secret with the corresponding account roles. Realtime account streams and
WebSocket order entry require an account-scoped key whose name starts with
`account-`.

## Supported functionality

- spot and perpetual instruments on `BoardCodes.Gemini` (`GEMINI`), including
  product type, trading state, price step, volume step, and minimum order size;
- Level1 REST snapshots plus the official live `bookTicker` stream;
- L2 REST snapshots and the full `depth@100ms` differential stream, including
  initial WebSocket snapshots, sequence validation, gap resubscription, local
  book maintenance, and reconnect restoration;
- up to 500 recent public trades per REST request plus the live trade stream;
- recent REST candles for all Gemini spot intervals and one-minute perpetual
  candles; live candles are built deterministically from the official trade
  stream because the current WebSocket API has no candle channel;
- REST balance, open-position, active-order, historical-order, and private-fill
  snapshots;
- authenticated `balances@account`, `positions@account`, and `orders@account`
  realtime streams;
- market, limit, stop-market, and stop-limit orders with GTC, IOC, FOK, and
  maker-or-cancel execution, individual cancellation, and account-wide bulk
  cancellation through WebSocket order methods;
- authenticated reconnect and restoration of public and private subscriptions.

Gemini's REST candle endpoints return a fixed recent window and do not accept a
date range. Public trade history is likewise limited to 500 trades per request;
the connector filters those responses to the requested StockSharp range but
does not claim unavailable deep history. A full security lookup requests symbol
details at the documented public rate limit and therefore completes
progressively.

The current WebSocket API authenticates during the HTTP upgrade and rejects
master or group keys. Such keys can still be used for private REST snapshots
with `Account` configured, but live account subscriptions and trading require
an `account-...` key. Trading writes are never retried automatically. Native
order replacement is not present in the current WebSocket contract. Gemini FIX
is a separate institutional interface and is not silently emulated by this
connector.

## Configuration

```csharp
var adapter = new GeminiMessageAdapter(new IncrementalIdGenerator())
{
    Key = "account-YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    IsCancelOnDisconnect = true,
};
```

The default production endpoints are `https://api.gemini.com` and
`wss://ws.gemini.com`. For the official sandbox, configure
`https://api.sandbox.gemini.com` and `wss://api.sandbox.gemini.com`. Endpoint
properties remain configurable for controlled routing.

## Official documentation

- [Gemini developer documentation](https://developer.gemini.com/)
- [REST API specification](https://developer.gemini.com/specs/openapi/rest.yaml)
- [Trading WebSocket specification](https://developer.gemini.com/specs/asyncapi/websocket.yaml)
- [API key authentication](https://developer.gemini.com/authentication/api-key)
- [StockSharp Gemini connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/gemini.html)

Gemini and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Gemini.
