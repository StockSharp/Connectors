# GMX connector for StockSharp

The connector integrates GMX V2 markets through the current official public
API v2. Market and token reference data, tickers, public executions, and candle
history work without credentials. A wallet address enables balance, position,
order, and execution monitoring. Express trading additionally requires the
matching wallet private key.

Every request and response is represented by a concrete DTO. The protocol
layer contains no generic JSON trees, anonymous request bodies, protocol
dictionaries, or dynamic payloads.

## Supported functionality

- perpetual and spot-swap security lookup with official token precision and
  market limits;
- Level1 oracle-price snapshots, open interest, and 24-hour prices;
- recent and live public executions through REST polling;
- historical and updating candles for `1m`, `5m`, `15m`, `1h`, `4h`, and
  `1d`;
- wallet balances, perpetual positions, active orders, and account execution
  history;
- EIP-712 signed express market, limit, stop-market, take-profit, stop-loss,
  and TWAP orders;
- order editing, individual cancellation, filtered bulk cancellation, and
  relay-status tracking;
- Arbitrum, Avalanche, and MegaETH production deployments with both official
  API peers.

The current official API exposes live state as REST snapshots and does not
publish a WebSocket feed. The connector therefore polls only active
subscriptions at the configured interval. GMX uses oracle and pool liquidity
rather than a central limit order book, so market-depth subscriptions are not
available.

For perpetual orders, StockSharp volume is the index-token quantity. The
connector converts it to GMX's 30-decimal USD notional at the order or current
mark price. For spot swaps, volume is the payment-token quantity. Explicit
collateral amounts are expressed in collateral-token units.

## Configuration

```csharp
var adapter = new GmxMessageAdapter(new IncrementalIdGenerator())
{
    Network = GmxNetworks.Arbitrum,
    WalletAddress = "0xYOUR_WALLET",
    PrivateKey = "0xYOUR_PRIVATE_KEY".ToSecureString(),
    DefaultCollateralToken = "USDC",
    DefaultLeverage = 5m,
    Slippage = 0.3m,
    PollingInterval = TimeSpan.FromSeconds(2),
};
```

Leave both wallet settings empty for public market data. Set only
`WalletAddress` for read-only account monitoring. When `PrivateKey` is set, its
signer address must match `WalletAddress`; if the wallet is omitted, the signer
address is used automatically. This connector intentionally does not configure
GMX subaccounts.

Express orders use the official prepare, EIP-712 sign, submit, and status
workflow. ERC-20 payment tokens must already have sufficient router allowance;
the connector does not send approval transactions. Trading writes are never
retried automatically. Safe public reads can fail over between the two official
API peers.

## Official documentation

- [GMX API overview](https://docs.gmx.io/docs/api/overview/)
- [GMX API integration guide](https://docs.gmx.io/docs/api/integration-guide/)
- [GMX API SDK v2](https://docs.gmx.io/docs/sdk/v2/)
- [GMX public API reference](https://docs.gmx.io/docs/api/gmx-api/gmx-io-gmx-public-api/)
- [Official GMX interface and SDK source](https://github.com/gmx-io/gmx-interface)
- [StockSharp GMX connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/gmx.html)

GMX and its marks are trademarks of their respective owner. StockSharp is not
affiliated with or endorsed by GMX.
