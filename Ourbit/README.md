# Ourbit connector for StockSharp

The connector integrates Ourbit spot and USDT-margined perpetual futures through
the exchange's current REST and WebSocket APIs. Public market data works without
credentials; portfolios, orders, fills, positions, and trading require an API
key and secret.

All REST requests, responses, WebSocket commands, and WebSocket events use
concrete DTOs. The protocol layer contains no dynamic JSON trees, anonymous
request bodies, or untyped protocol collections.

## Supported functionality

- spot instruments on `BoardCodes.Ourbit` (`OURBT`);
- perpetual futures on `BoardCodes.OurbitFutures` (`OURBF`);
- security lookup with price step, volume step, minimum volume, and futures
  contract multiplier metadata;
- Level1, L2 order books, tick trades, and exchange candles through REST and
  official WebSocket streams;
- order-book snapshot recovery after a futures sequence gap;
- spot balances and futures balances and positions;
- active and historical orders and fills;
- limit, market, post-only, IOC, and FOK orders where supported by the selected
  market;
- individual and group cancellation;
- private spot account, order, and fill streams;
- private futures asset, position, order, and fill streams;
- heartbeat, reconnect, and subscription restoration.

Ourbit does not expose an atomic order-replace operation, so replacement is
reported as unsupported. Trading writes are never retried automatically. If a
write fails after it may have reached the exchange, inspect exchange state
before submitting it again.

## Configuration

```csharp
var adapter = new OurbitMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [OurbitSections.Spot, OurbitSections.Futures],
};
```

Default endpoints:

- spot REST: `https://api.ourbit.com`;
- spot WebSocket: `wss://wbs.ourbit.com/ws`;
- futures REST: `https://futures.ourbit.com/api/v1`;
- futures WebSocket: `wss://futures.ourbit.com/edge`.

The spot WebSocket permits at most 30 public channels on one connection. The
connector enforces this limit. Futures quantities are exchange contract units;
the instrument multiplier is published through StockSharp security metadata.

## Official documentation

- [Ourbit Spot API](https://ourbitdevelop.github.io/apidocs/spot_v3_en/)
- [Ourbit Futures API](https://ourbitdevelop.github.io/apidocs/contract_en/)
- [Official Ourbit API examples](https://github.com/ourbitdevelop/ourbit-api-demo)
- [StockSharp Ourbit connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/ourbit.html)

Ourbit and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Ourbit.
