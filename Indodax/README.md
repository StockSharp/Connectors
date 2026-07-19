# Indodax Connector

The connector integrates Indodax spot markets through the official public REST,
Trade API v2, TAPI, Market Data WebSocket, and Private WebSocket protocols.

Supported features:

- discovery of all spot pairs with exchange price and volume limits;
- Level1 quotes, order-book snapshots, public trades, and historical candles;
- realtime books and trades through the official Market Data WebSocket;
- balances, open orders, completed-order history, and account fill history;
- realtime private order and fill events through the official Private WebSocket;
- limit and market orders, client order IDs, maker-only limit orders, individual
  cancellation, and filtered group cancellation;
- HMAC-SHA512 authentication, public/trading/cancellation rate limits,
  exchange-clock adjustment, reconnect with channel recovery, fresh private
  tokens on reconnect, and lost-placement-response reconciliation.

An API key is optional for public market data. Trading, balances, histories, and
the private stream require an Indodax TAPI key and secret. A market buy uses
`IndodaxOrderCondition.QuoteAmount`, because Indodax accepts market-buy size in
quote currency; market sells use regular order volume. Set `PostOnly` on a limit
order to request Indodax `MOC` maker-only execution.

Order and fill histories use the dedicated Trade API v2 endpoints. The legacy
`tradeHistory` and `orderHistory` TAPI methods are not used because Indodax
decommissioned them on April 7, 2026.

Every REST and WebSocket payload is represented by a concrete DTO. Array-based
book and trade records and currency-keyed account maps are decoded by typed
converters; the transport does not use loose JSON protocol structures.

Official resources:

- [Indodax API documentation](https://github.com/btcid/indodax-official-api-docs)
- [Indodax website](https://indodax.com/)
