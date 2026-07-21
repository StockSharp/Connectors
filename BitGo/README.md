# StockSharp BitGo Connector

The connector integrates StockSharp with BitGo Prime trading. It uses the
authenticated Prime REST API for accounts, products, balances, order entry,
cancellation, and history, and the official Trade WebSocket for Level 2 order
books and private order lifecycle events.

## Access and configuration

Configure a BitGo access token with the `trade_view` scope. Order entry also
requires `trade_trade`. If the token can access more than one Prime account,
set `Account` to the exact account ID or name; a single available account is
selected automatically.

Production defaults are:

- REST: `https://app.bitgo.com/`
- WebSocket: `wss://app.bitgo.com/api/prime/trading/v1/ws`

For test use, replace `app.bitgo.com` with `app.bitgo-test.com` in both
settings. `ApiEndpoint` can also point to BitGo Express, which proxies the
same Prime REST paths. The WebSocket remains a direct BitGo connection. The
access token is stored as a secure string and is sent only in the HTTP or
WebSocket handshake `Authorization: Bearer` header.

## Protocol coverage

The connector supports:

- Prime account selection and product discovery;
- real-time Level 1 and aggregated Level 2 books from snapshot plus
  incremental WebSocket updates;
- account balance snapshots and periodic reconciliation;
- market, limit, stop-market, and stop-limit orders;
- regular and time-sliced TWAP orders;
- Steady Pace orders with interval, child-size, and variance controls;
- funded and margin order routing when enabled for the product;
- GTC, GTD, IOC, and FOK instructions where accepted by BitGo;
- single and filtered group cancellation;
- live private order and fill events;
- paginated REST order and trade history.

StockSharp order volume is submitted in the product's base currency. BitGo
also permits quote-currency quantities, but using base quantity preserves the
framework's order-volume semantics. BitGo does not expose REST replace; use
cancel followed by a new order. The adapter reconciles live WebSocket state
with REST snapshots without retrying order mutations.

Custody wallet creation, wallet policies, blockchain transfers, transaction
signing, staking, settlement webhooks, and administrative APIs are outside
this trading adapter. They remain available through the broader BitGo API and
BitGo Express.

All protocol requests and responses use explicit DTOs. The order-book tuple
converter reads the documented `[price, size]` wire format directly; JSON
trees, dynamic payloads, anonymous protocol objects, protocol dictionaries,
and untyped object arrays are not used. API timestamps are normalized to UTC
`DateTime` values.

## Official resources

- [BitGo Developer Portal](https://developers.bitgo.com/)
- [Prime trading WebSocket overview](https://developers.bitgo.com/reference/websocket-overview/)
- [Level 2 WebSocket channel](https://developers.bitgo.com/reference/tradewebsocketlevel2/)
- [Order WebSocket channel](https://developers.bitgo.com/reference/tradewebsocketorders/)
- [Place Order API](https://developers.bitgo.com/reference/tradeordersadd/)
- [Place Trade Orders guide](https://developers.bitgo.com/docs/trade-funded-place-orders/)
