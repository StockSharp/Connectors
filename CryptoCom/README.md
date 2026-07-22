# Crypto.com Exchange connector for StockSharp

The connector integrates the current Crypto.com Exchange REST API with the official
public market-data and authenticated user WebSocket streams. Spot, perpetual-swap,
and dated-futures instruments are exposed through the StockSharp message model.

## Supported functionality

- spot instruments on `BoardCodes.CryptoCom` (`CRCOM`);
- perpetual swaps and dated futures on `BoardCodes.CryptoComDerivatives` (`CRCOD`);
- security lookup, Level1, recent trades, incremental order books, and candles;
- official public WebSocket channels for tickers, trades, books, and candles;
- REST history paging within the exchange limits of 150 trades and 300 candles;
- limit, market, stop-loss, stop-limit, take-profit, and take-profit-limit orders;
- order amendment, individual cancellation, and filtered group cancellation;
- post-only, reduce-only, cross-margin, and isolated-margin order parameters;
- balances, collateral balances, derivatives positions, and private order/fill
  updates through the authenticated user WebSocket;
- HMAC-SHA256 request signing, UAT endpoints, heartbeat responses, reconnect, and
  automatic WebSocket subscription restoration.

Trading writes are never automatically retried. When a write fails after it could
have reached the exchange, check the order state before submitting another request.
Safe public and private reads use bounded retry with rate-limit backoff.

## Configuration

Public market data works without credentials when the adapter is configured as a
market-data-only adapter. Set `Key` and `Secret` for transactions, portfolios, and
private streams. `Sections` selects spot, derivatives, or both. Set `IsDemo` to use
the official UAT environment.

```csharp
var adapter = new CryptoComMessageAdapter(new IncrementalIdGenerator())
{
    Key = "YOUR_API_KEY".ToSecureString(),
    Secret = "YOUR_API_SECRET".ToSecureString(),
    Sections = [CryptoComSections.Spot, CryptoComSections.Derivatives],
    IsDemo = false,
};
```

Production endpoints:

- REST: `https://api.crypto.com/exchange/v1`;
- market WebSocket: `wss://stream.crypto.com/exchange/v1/market`;
- user WebSocket: `wss://stream.crypto.com/exchange/v1/user`.

UAT endpoints:

- REST: `https://uat-api.3ona.co/exchange/v1`;
- market WebSocket: `wss://uat-stream.3ona.co/exchange/v1/market`;
- user WebSocket: `wss://uat-stream.3ona.co/exchange/v1/user`.

## Official documentation

- [Crypto.com Exchange API](https://exchange-developer.crypto.com/exchange/v1)
- [REST common API reference](https://exchange-developer.crypto.com/exchange/v1/docs/api/rest-common-api-reference)
- [StockSharp Crypto.com Exchange connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/crypto_com.html)

Crypto.com and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Crypto.com.
