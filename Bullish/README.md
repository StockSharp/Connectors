# Bullish connector for StockSharp

The connector integrates Bullish spot and derivatives markets through the
current official Trading API. REST supplies instruments, snapshots, historical
data, portfolios, positions, orders, and fills. The current multi-market
WebSocket services provide L2 order books, anonymous trades, ticks, and private
account events.

## Supported functionality

- spot markets on `BoardCodes.Bullish` (`BULL`);
- perpetuals, dated futures, and options on
  `BoardCodes.BullishDerivatives` (`BULLD`);
- security lookup with expiry, strike, option type, price step, quantity step,
  and contract multiplier metadata;
- Level1, reconstructed L2 order books, recent and historical trades, and
  historical candles;
- official `tick` WebSocket subscriptions for perpetual Level1 data and
  `anonymousTrades` plus `l2Orderbook` streams for the remaining markets;
- live candle updates aggregated from the official anonymous-trade stream;
- limit, market, stop-limit, post-only, IOC, FOK, and auction GTX orders where
  the selected Bullish market supports them;
- order amendment, individual cancellation, market-wide cancellation, and
  account-wide cancellation;
- multiple trading accounts, asset balances, derivatives positions, active and
  historical orders, and fills;
- authenticated order, fill, asset-account, trading-account, and derivatives
  position WebSocket updates;
- current HMAC API-key login, automatically renewed JWT-cookie sessions,
  v2 command signing, optional institutional rate-limit tokens, heartbeat,
  reconnect, subscription restoration, and order-book gap recovery.

Trading writes are never retried automatically. If a write fails after it may
have reached Bullish, inspect exchange state before submitting it again.

## Configuration

Public market data works without credentials. Private operations require a
Bullish HMAC API key and secret. `TradingAccountId` is optional; the primary
trading account is selected when it is empty. `RateLimitToken` may be supplied
for institutional accounts, otherwise the token returned for the selected
trading account is used when available.

```csharp
var adapter = new BullishMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_HMAC_PUBLIC_KEY".ToSecureString(),
    Secret = "YOUR_HMAC_SECRET".ToSecureString(),
    TradingAccountId = "YOUR_TRADING_ACCOUNT_ID",
    Sections = [BullishSections.Spot, BullishSections.Derivatives],
};
```

Default endpoints:

- REST: `https://api.exchange.bullish.com`;
- public and private WebSocket: `wss://api.exchange.bullish.com`.

Bullish documents SimNext sandbox hosts. They can be selected by replacing both
endpoint settings with the corresponding official sandbox hosts.

## Official documentation

- [Bullish Developer Docs](https://docs.exchange.bullish.com/)
- [Bullish REST API](https://docs.exchange.bullish.com/rest/introduction/)
- [Bullish WebSocket API](https://docs.exchange.bullish.com/websocket/protocol/connectivity/)
- [Official Bullish API examples](https://github.com/bullish-exchange/api-examples)
- [StockSharp Bullish connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/bullish.html)

Bullish and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Bullish.
