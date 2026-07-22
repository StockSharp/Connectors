# StockSharp Aevo connector

The connector integrates StockSharp with Aevo's options, perpetuals and spot
API. It uses the official REST and WebSocket interfaces and keeps all protocol
payloads in typed models.

## Supported operations

- security discovery for all instruments returned by Aevo;
- current and streaming Level1 data, including mark/index prices, top of book,
  open interest and option Greeks;
- recent public trades and live trade subscriptions;
- REST order-book snapshots followed by throttled incremental WebSocket
  updates with checksum validation;
- account equity, collateral balances and derivative positions;
- open and historical orders, account fills and private real-time updates;
- signed market and limit orders, atomic order replacement, individual
  cancellation and filtered or complete bulk cancellation;
- mainnet and Sepolia testnet environments.

Aevo does not expose OHLC candle history through this API. Candles can be built
from the connector's tick stream by StockSharp instead of presenting mark-price
samples as exchange candles.

## Configuration

Public market data requires no credentials. Account data requires `Key` and
`Secret`. `WalletAddress` is optional when the authenticated account should
be discovered from the API.

Trading additionally requires `SigningKey`, the EVM private signing key
registered with Aevo. Orders are signed locally using Aevo's EIP-712 domain and
fixed six-decimal wire representation. The private key is never sent to Aevo.

The default endpoints are:

- `https://api.aevo.xyz`
- `wss://ws.aevo.xyz`
- `https://api-testnet.aevo.xyz`
- `wss://ws-testnet.aevo.xyz`

Both endpoint settings can be overridden for compatible gateways.

## Documentation

- [Aevo API documentation](https://api-docs.aevo.xyz/)
- [REST authentication](https://api-docs.aevo.xyz/reference/rest-authentication)
- [WebSocket authentication](https://api-docs.aevo.xyz/reference/websocket-authentication)
- [Order signing](https://api-docs.aevo.xyz/reference/signing-orders)
