# Deepcoin connector for StockSharp

The connector integrates Deepcoin spot and perpetual markets through the
official V2 REST and WebSocket APIs. Public market data works without
credentials. Trading, balances, positions, orders, and fills require an API
key, secret, and passphrase.

## Supported functionality

- spot and perpetual instruments on `BoardCodes.Deepcoin` (`DEEP`);
- security lookup with price and volume steps, limits, and contract values;
- Level1 snapshots and official live ticker channels;
- L2 snapshots plus official full and incremental order-book channels;
- recent and live trades;
- historical candles and official live candle channels;
- separate spot and perpetual portfolios, balances, and positions;
- active and historical orders and fills;
- limit, market, post-only, and IOC orders;
- order amendment plus individual and filtered batch cancellation;
- cash, cross-margin, and isolated-margin modes, merged and split positions,
  leverage, reduce-only, and attached take-profit/stop-loss parameters;
- authenticated account, position, order, and fill WebSocket channels;
- heartbeat, reconnect, and subscription restoration.

Trading writes are never retried automatically. If a write fails after it may
have reached the exchange, inspect exchange state before submitting it again.

## Configuration

```csharp
var adapter = new DeepcoinMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Passphrase = "YOUR_API_PASSPHRASE".ToSecureString(),
};
```

Default production endpoints:

- REST: `https://api.deepcoin.com`;
- spot public WebSocket:
  `wss://stream.deepcoin.com/streamlet/trade/public/spot?platform=api&version=v2`;
- perpetual public WebSocket:
  `wss://stream.deepcoin.com/streamlet/trade/public/swap?platform=api&version=v2`;
- private WebSocket: `wss://stream.deepcoin.com/v1/private`.

The private WebSocket connection obtains and renews its listen key through the
V2 REST API. Endpoint properties remain configurable for compatible Deepcoin
environments and controlled routing.

## Official documentation

- [Deepcoin V2 authentication](https://www.deepcoin.com/docs/v2/authentication)
- [Deepcoin V2 public WebSocket API](https://www.deepcoin.com/docs/v2/publicWS/public)
- [StockSharp Deepcoin connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/deepcoin.html)

Deepcoin and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Deepcoin.
