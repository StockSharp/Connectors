# Reya connector for StockSharp

The connector integrates Reya DEX spot and perpetual markets through the
current official REST API v2 and WebSocket API v2. Public reference data,
prices, summaries, order books, executions, and candle history work without
credentials. A wallet address enables account, balance, position, order, and
execution monitoring. EIP-712 signed trading additionally requires a signer
private key and a Reya account ID; the owner and signer wallets may differ when
Reya delegation is configured.

Every REST request, response, WebSocket command, event, order-book level, and
candle series is represented by a concrete DTO. The protocol layer contains no
generic JSON trees, anonymous request bodies, protocol dictionaries, or dynamic
payloads.

## Supported functionality

- spot and perpetual security lookup with official price and quantity rules;
- REST snapshots and live WebSocket updates for Level1 data;
- spot L2 order-book snapshots and incremental level changes;
- recent and live public executions;
- historical candles for every resolution exposed by Reya, with live candles
  aggregated from the official execution stream;
- wallet accounts, asset balances, perpetual positions, open orders, and fills;
- live balance, position, order, and private execution streams;
- EIP-712 signed limit and protected market orders, IOC and GTC, reduce-only,
  stop-loss, and take-profit orders;
- EIP-191 perpetual cancellation, EIP-712 spot cancellation, and filtered bulk
  cancellation without retrying trading writes.

Reya stop-loss and take-profit requests are full-position protective orders:
the API does not accept a quantity for those order types. Perpetual L2 depth is
not exposed by the current API, so order-book subscriptions are spot-only.

## Configuration

```csharp
var adapter = new ReyaMessageAdapter(new IncrementalIdGenerator())
{
    WalletAddress = "0xYOUR_OWNER_WALLET",
    AccountId = "YOUR_REYA_ACCOUNT_ID",
    PrivateKey = "0xYOUR_SIGNER_PRIVATE_KEY".ToSecureString(),
};
```

Leave all three values empty for public market data. Set `WalletAddress` for
read-only account monitoring. If `AccountId` is empty, the connector discovers
the wallet's official spot and perpetual account IDs. Set `PrivateKey` only for
trading; when `WalletAddress` is empty, the signer address is also used as the
owner address.

The production endpoints default to `https://api.reya.xyz/v2` and
`wss://ws.reya.xyz/`. Chain ID, Orders Gateway contract, exchange ID, pool
account ID, REST endpoint, and WebSocket endpoint remain configurable for
testnet or controlled routing.

## Official documentation

- [Reya DEX REST API v2](https://docs.reya.xyz/technical-docs/reya-dex-rest-api-v2)
- [REST and WebSocket specifications](https://docs.reya.xyz/technical-docs/reya-dex-rest-api-v2/specs)
- [Reya DEX WebSocket API v2](https://docs.reya.xyz/technical-docs/reya-dex-websocket-api-v2)
- [Official Python SDK](https://github.com/Reya-Labs/reya-python-sdk)
- [StockSharp Reya connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/reya.html)

Reya and its marks are trademarks of their respective owner. StockSharp is not
affiliated with or endorsed by Reya.
