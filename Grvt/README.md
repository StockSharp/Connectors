# GRVT Connector

The connector integrates GRVT's official full-field REST and WebSocket APIs.
Public market data can be used without credentials. Private account data uses
an API-key login and the resulting session cookie, while order submission also
requires the EVM private key associated with the GRVT signer. The private key
is used locally for EIP-712 signing and is never sent to GRVT.

Supported functionality:

- perpetual, future, call, and put instrument discovery;
- Level1, order-book snapshots, public trades, and candles over WebSocket;
- historical public trades and candles through paginated REST requests;
- account balances, positions, open orders, order history, and fills;
- locally signed market, limit, post-only, IOC, FOK, reduce-only, and TP/SL
  orders;
- individual and filtered bulk cancellation;
- authenticated order, fill, and position streams;
- production and testnet environments.

`Key` is the API key created in the GRVT UI. `Secret` is the matching EVM
private key and is needed only for order creation. `SubAccountId` may be left
empty when the API-key login response identifies a trading account. Endpoint
properties default to the official hosts and switch together when
`Environment` changes.

The connector uses the snapshot variants of the ticker and order-book streams.
It checks stream sequence numbers, ignores duplicates, and reports gaps instead
of inventing missing data. All protocol payloads use concrete DTOs; the
transport contains no dynamic JSON trees, anonymous protocol payloads, or
protocol dictionaries.

Official resources:

- [GRVT API documentation](https://api-docs.grvt.io/)
- [Market Data API](https://api-docs.grvt.io/market_data_api/)
- [Market Data WebSocket](https://api-docs.grvt.io/market_data_streams/)
- [Trading API](https://api-docs.grvt.io/trading_api/)
- [Trading WebSocket](https://api-docs.grvt.io/trading_streams/)
- [Authentication](https://api-docs.grvt.io/auth/)
- [Official Python SDK](https://github.com/gravity-technologies/grvt-pysdk)
