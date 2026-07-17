# SnapTrade Connector for StockSharp

This connector integrates StockSharp with the current SnapTrade API. SnapTrade is a broker-aggregation service: availability, data freshness, supported asset classes, and trading features depend on the connected brokerage and the API plan.

## Features

- Personal and Commercial API-key authentication with canonical HMAC-SHA256 request signing.
- Brokerage-account discovery and explicit account selection.
- Account-specific symbol search for broker-supported equities.
- Cash balances, buying power, account value, and normalized positions across the instrument kinds returned by SnapTrade.
- Recent order history and rate-aware order-state polling.
- Stock and ETF market, limit, stop, and stop-limit orders.
- Day, GTC, GTD, IOC, and FOK instructions where the connected brokerage supports them.
- Extended-hours limit orders, notional market orders, cancellation, and replacement where supported by the brokerage.
- Broker quote retrieval as REST-polled StockSharp Level 1 data.

All request and response bodies use typed DTOs. Safe reads and the symbol-search request retry transient transport errors, HTTP 429, and server failures. Trading, cancel, and replace requests are never automatically retried because SnapTrade does not guarantee idempotency across every connected brokerage.

## Configuration

- `ClientId` - the client ID from a Personal or Commercial SnapTrade API key.
- `ConsumerKey` - the secret used for HMAC-SHA256 request signatures.
- `UserId` and `UserSecret` - required together for a Commercial app user. Leave both empty when the API key is Personal and represents the authenticated user directly.
- `AccountId` - the SnapTrade brokerage-account UUID or account number. It may be omitted only when one usable investment account is available.
- `PollingInterval` - minimum spacing between polling jobs. The default is one minute and values below 15 seconds are clamped.

SnapTrade uses the same API origin for Personal, Commercial test, and production keys. Paper trading is provided by a connected brokerage account, such as a broker's paper environment, rather than by a separate SnapTrade base URL.

## Order mapping

`OrderRegisterMessage.Volume` is sent as the number of units. `SnapTradeOrderCondition.NotionalValue` replaces units with a cash amount and is valid only for market/day orders. `StopPrice` selects stop or stop-limit routing for a conditional order. `IsGoodTillCanceled` selects GTC when no explicit expiry is set, while `TillDate` selects GTD. `IsExtendedHours` is accepted only for limit orders.

The force-order endpoint is used because a StockSharp order message is already an execution instruction. Applications that need an end-user preview and confirmation screen should call SnapTrade's checked-trade workflow in their own UI before submitting the StockSharp order.

The normalized order response provides cumulative filled quantity and an execution price, but not individual brokerage fill identifiers. The connector therefore publishes incremental aggregate fill changes and does not invent native trade IDs.

## Polling, quotes, and webhooks

SnapTrade does not expose a trading WebSocket. Its webhook events are delivered to an HTTPS endpoint operated by the API customer, which a local message adapter cannot host or register implicitly. This connector consequently polls subscribed data.

Polling is deliberately rate-aware. At most one workload category is selected per interval: orders, portfolio data, or one quote batch. Portfolio data uses the balances and all-positions endpoints. Live order polls alternate the lightweight last-24-hours endpoint with an open-order query so older GTC orders remain visible. Quote batches contain at most 10 symbols. When several categories are active, they rotate, so each individual subscription refreshes less frequently than `PollingInterval`.

The quote endpoint can be delayed, is disabled on some plans, and SnapTrade explicitly says it is not a substitute for a market-data provider. The connector advertises only Level 1 for these snapshots; it does not claim ticks, order books, candles, or streaming data.

## Scope limitations

- Direct order routing is limited to stocks and ETFs. Options, futures, crypto, CFDs, and funds returned by the all-positions endpoint remain visible as positions, but their separate trading workflows are not presented as equity orders.
- Symbol search returns at most the first 20 account-supported matches, so full security-master download is not advertised.
- Exact capabilities, market hours, fractional support, replacement support, and freshness vary by brokerage.
- SnapTrade account activities are generally daily data and are not used as an intraday execution feed.

## Documentation

- [StockSharp SnapTrade connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/snaptrade.html)
- [Official SnapTrade documentation](https://docs.snaptrade.com/)
- [Authentication methods](https://docs.snaptrade.com/docs/authentication-methods)
- [Request signatures](https://docs.snaptrade.com/docs/request-signatures)
- [Account data](https://docs.snaptrade.com/docs/account-data)
- [Trading with SnapTrade](https://docs.snaptrade.com/docs/trading-with-snaptrade)
- [Rate limiting](https://docs.snaptrade.com/docs/ratelimiting)
- [Webhooks](https://docs.snaptrade.com/docs/webhooks)
- [Get equity symbol quotes](https://docs.snaptrade.com/reference/Trading/Trading_getUserAccountQuotes)
- [Place equity order](https://docs.snaptrade.com/reference/Trading/Trading_placeForceOrder)
- [Cancel order](https://docs.snaptrade.com/reference/Trading/Trading_cancelOrder)
- [Replace order](https://docs.snaptrade.com/reference/Trading/Trading_replaceOrder)
