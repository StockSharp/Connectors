# Bitso connector for StockSharp

The connector integrates Bitso's current Trading API for the Latin American
spot market. Public instruments and market data work without credentials.
Balances, orders, replacements, cancellations, and user fills require a Bitso
API key and secret with the corresponding permissions.

## Supported functionality

- all books from `GET /available_books` on `BoardCodes.Bitso` (`BITSO`), with
  major/minor assets, tick size, amount limits, and quote currency;
- REST Level1 snapshots with bid, ask, last, 24-hour high, low, volume, change,
  and VWAP;
- aggregated REST order-book snapshots and realtime top-20 `orders` WebSocket
  snapshots;
- recent public trades followed by the realtime `trades` WebSocket channel;
- account balances with available and locked amounts;
- open-order and recent user-fill snapshots, plus REST polling for private
  updates while portfolio or order-status subscriptions are active;
- spot market, limit, stop-market, and stop-limit orders;
- GTC, IOC, FOK, and post-only mapping, optional market-order slippage
  tolerance, native v4 in-place modification, individual cancellation, and
  filtered bulk cancellation;
- Nonce v2 values, exact HMAC-SHA256 request signing, documented public/private
  rate limits, safe-read retries, and no automatic retries for trading writes.

The current Bitso Trading API does not publish a candle-history endpoint, so
the connector does not advertise candle support. Its WebSocket API documents
subscriptions but no unsubscribe command. The connector therefore stops
locally delivering a released stream and prunes server subscriptions on the
next reconnect.

## Configuration

```csharp
var adapter = new BitsoMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
};
```

Set `IsDemo = true` to use Bitso's official sandbox REST host. The public
WebSocket market-data host is shared and remains independently configurable.

## Official documentation

- [Bitso Trading API overview](https://docs.bitso.com/bitso-api/docs/api-overview)
- [Bitso WebSocket API](https://docs.bitso.com/bitso-api/docs/general)
- [Bitso Nonce v2](https://docs.bitso.com/bitso-api/docs/nonce-v2-rollout)
- [Bitso website](https://bitso.com/)
- [StockSharp Bitso connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/bitso.html)

Bitso and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Bitso.
