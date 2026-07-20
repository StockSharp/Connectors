# StockSharp Bluefin connector

The connector integrates StockSharp with the current Bluefin Pro perpetuals
API on Sui. It uses the official REST and WebSocket interfaces and represents
all protocol payloads with typed models.

## Supported operations

- perpetual market discovery and trading-state metadata;
- current and streaming Level1 data, including last, mark and oracle prices,
  top of book, 24-hour statistics and open interest;
- recent public trades and live trade subscriptions;
- REST order-book snapshots followed by sequenced incremental WebSocket
  updates with automatic recovery after a sequence gap;
- historical and live last-price candles for every interval exposed by
  Bluefin Pro;
- account value, collateral balances and perpetual positions;
- open orders, account fills and private real-time account updates;
- locally signed market, limit, stop and take-profit orders, cancellation,
  replacement and filtered bulk cancellation;
- production and staging environments.

The current Bluefin Pro API publishes perpetual markets. It does not expose a
spot order book through these endpoints, so the connector does not label
perpetual instruments as spot securities.

## Configuration

Public market data requires no credentials. `WalletAddress` is sufficient for
the public account snapshot endpoint. Private streams, order history and
trading require `PrivateKey`, supplied as either a Sui `suiprivkey` Ed25519 key
or 32 hexadecimal seed bytes. The connector derives and validates the Sui
address and never sends the private key to Bluefin.

Authentication and orders are signed locally using Sui personal-message
signing: BCS byte-vector serialization, the personal-message intent and
Blake2b-256. Signed numeric order fields use Bluefin's fixed e9 format.

The default production endpoints are:

- `https://api.sui-prod.bluefin.io`
- `https://trade.api.sui-prod.bluefin.io`
- `https://auth.api.sui-prod.bluefin.io`
- `wss://stream.api.sui-prod.bluefin.io/ws/market`
- `wss://stream.api.sui-prod.bluefin.io/ws/account`

Each endpoint can be overridden independently for staging, compatible gateways
or colocated infrastructure.

## Documentation

- [Bluefin API documentation](https://bluefin-exchange.readme.io/)
- [Official Bluefin Pro SDK](https://github.com/fireflyprotocol/pro-sdk)
