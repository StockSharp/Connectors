# Tapbit Connector

The connector integrates Tapbit Spot V2 and USDT perpetual markets through
the current production REST and WebSocket APIs.

Supported features:

- discovery of Spot and USDT perpetual instruments with native price,
  quantity, contract-multiplier, leverage, and order-size metadata;
- Level1 snapshots and realtime ticker updates, including mark price, index
  price, and open interest where Tapbit publishes them;
- REST order-book snapshots and stateful realtime order books built from the
  documented WebSocket snapshot and incremental-update protocol;
- recent public trades and realtime delivery through bounded REST polling;
- historical OHLCV candles and realtime candle updates for every interval
  published by Tapbit;
- Spot V2 API-key authentication, balances, limit-order registration,
  individual and batch cancellation, open orders, historical orders, and
  order lookup;
- safe read retries, exact-query signing, documented request pacing,
  WebSocket subscription recovery, heartbeat replies, and response limits.

Public market data does not require credentials. Private Spot operations
require a Tapbit API key and secret, KYC eligibility, the permissions selected
for that key, and any IP whitelist configured in the Tapbit firewall.

Tapbit's current public USDT perpetual documentation exposes market-data REST
and WebSocket endpoints, but no private account, position, or order contract.
Consequently, this connector provides futures market data and implements
trading only for the officially documented Spot V2 API. Futures trading is not
emulated through undocumented or reverse-engineered endpoints. Tapbit states
that futures API access is application-based and subject to eligibility
requirements.

The Spot V2 order endpoint documents limit orders only and does not expose a
client-order ID, replace operation, or private execution stream. The connector
therefore rejects unsupported order types and polls the documented REST order
state instead of inventing protocol fields.

Every REST and WebSocket payload is represented by a concrete DTO. The
transport does not use dynamic JSON trees, anonymous protocol objects,
protocol dictionaries, or untyped object arrays.

Official resources:

- [Tapbit Open API documentation](https://www.tapbit.com/openapi-docs/)
- [Spot V2 base endpoint](https://www.tapbit.com/openapi-docs/spot_v2/general_info/base_endpoint/)
- [Spot V2 signing](https://www.tapbit.com/openapi-docs/spot_v2/general_info/signature_method/)
- [Spot V2 market data](https://www.tapbit.com/openapi-docs/spot_v2/public/trade_pair_list/)
- [Spot V2 place order](https://www.tapbit.com/openapi-docs/spot_v2/private/order/)
- [Spot WebSocket order book](https://www.tapbit.com/openapi-docs/spot/ws/order_book/)
- [USDT perpetual market data](https://www.tapbit.com/openapi-docs/usdt_perpetual/public/exchange_info/)
- [USDT perpetual WebSocket](https://www.tapbit.com/openapi-docs/usdt_perpetual/ws/ws_general_info/)
