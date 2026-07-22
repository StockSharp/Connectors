# BloFin connector for StockSharp

The connector integrates BloFin perpetual futures through the exchange's
official OpenAPI. Public market data works without credentials. Trading,
portfolio state, orders, fills, and positions require an API key, secret, and
passphrase.

## Supported functionality

- perpetual instruments on `BoardCodes.BloFin` (`BLOFN`);
- security lookup with price and volume steps, limits, and contract value;
- Level1 snapshots and the official live ticker channel;
- L2 snapshots plus the `books5` snapshot and `books` incremental channels;
- recent and live trades;
- historical candles and official live candle channels;
- account balances and perpetual positions;
- active and historical orders and fills;
- limit, market, post-only, IOC, and FOK orders;
- cross and isolated margin, hedge position sides, leverage, reduce-only, and
  attached take-profit/stop-loss parameters;
- individual and filtered batch cancellation;
- authenticated account, position, and order WebSocket channels;
- heartbeat, sequence-gap recovery, reconnect, and subscription restoration;
- production and official demo-trading endpoints.

The current BloFin documentation fully specifies perpetual-futures market data
and trading. It exposes a private spot fill-history method, but no complete
public spot instrument, market-data, and order API. The connector therefore
does not present an incomplete spot implementation as a supported market.

Trading writes are never retried automatically. If a write fails after it may
have reached the exchange, inspect exchange state before submitting it again.

## Configuration

```csharp
var adapter = new BloFinMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Passphrase = "YOUR_API_PASSPHRASE".ToSecureString(),
    IsDemo = false,
};
```

Default production endpoints:

- REST: `https://openapi.blofin.com`;
- public WebSocket: `wss://openapi.blofin.com/ws/public`;
- private WebSocket: `wss://openapi.blofin.com/ws/private`.

When `IsDemo` is enabled, the connector switches all three endpoints to the
official `demo-trading-openapi.blofin.com` service. BloFin applies geographic
access restrictions; an HTTP restriction response must be resolved with the
exchange and is not treated as a protocol or JSON error.

## Official documentation

- [BloFin API documentation](https://docs.blofin.com/)
- [Official BloFin Python SDK](https://github.com/blofin/blofin-sdk-python)
- [StockSharp BloFin connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/blofin.html)

BloFin and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by BloFin.
