# Toobit connector for StockSharp

The connector integrates Toobit's current REST API and official public and private
WebSocket streams with the StockSharp message model. Spot and USDT-margined futures
are exposed as separate boards while sharing one adapter configuration.

## Supported functionality

- spot instruments on `BoardCodes.Toobit` (`TOOBT`);
- USDT-margined perpetual contracts on `BoardCodes.ToobitFutures` (`TOOBF`);
- Level1 statistics, recent trades, full order-book snapshots, and time-frame candles;
- live public market data through `wss://stream.toobit.com/quote/ws/v1`;
- spot and futures order registration, replacement, individual cancellation, and
  filtered group cancellation;
- limit, market, post-only, and futures trigger orders;
- spot balances, futures balances and positions;
- private order, fill, balance, and position updates through listen-key WebSockets;
- server-time synchronization, HMAC-SHA256 signing, listen-key renewal, and the
  documented WebSocket heartbeat;
- rate-limit backoff using `X-Api-Limit-Reset-Timestamp` for safe requests.

Toobit marks the outcome of a trading request that ends with a transport error or an
HTTP 5xx response as unknown. Consequently the connector never retries order writes
automatically. Check order state before deciding whether another write is safe.

## Configuration

Set `Key` and `Secret` when transactions, portfolios, or private streams are needed.
Public market data works without credentials. `Sections` selects `Spot`, `Futures`,
or both. The production endpoints are:

- `RestEndpoint`: `https://api.toobit.com`;
- `WsEndpoint`: `wss://stream.toobit.com/quote/ws/v1`.

```csharp
var adapter = new ToobitMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [ToobitSections.Spot, ToobitSections.Futures],
};
```

Futures market orders follow Toobit's v1 contract: the request uses `type=LIMIT`
with `priceType=MARKET`. The connector performs that conversion internally. A
futures close order is selected with `OrderPositionEffects.CloseOnly`. Spot market
orders pass StockSharp `Volume` as Toobit's documented `quantity` parameter.

The recent-trades REST endpoint exposes at most 60 records. Longer historical tick
requests therefore cannot be reconstructed by REST; live continuation uses the
trade WebSocket. Candle history is paged in batches of up to 1000 records.

## Official documentation

- [Toobit API documentation](https://api-docs.toobit.com/)
- [API security, signing, timing, and rate limits](https://api-docs.toobit.com/api/basic-information.html)
- [Spot market data](https://api-docs.toobit.com/api/spot-market-data.html)
- [Public WebSocket market streams](https://api-docs.toobit.com/api/spot-websocket-market-data.html)
- [Spot account and trading](https://api-docs.toobit.com/api/spot-account-and-trading.html)
- [Spot user streams](https://api-docs.toobit.com/api/spot-websocket-account.html)
- [USDT-M account and trading](https://api-docs.toobit.com/api/usdt-m-account-and-trading.html)
- [USDT-M user streams](https://api-docs.toobit.com/api/usdt-m-websocket-account.html)
- [StockSharp Toobit connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/toobit.html)

Toobit and its marks are trademarks of their respective owner. StockSharp is not
affiliated with or endorsed by Toobit.
