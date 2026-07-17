# Match-Trader Connector for StockSharp

This connector integrates StockSharp with the current Match-Trader Platform API exposed by a participating broker or prop-trading provider. Match-Trade Technologies supplies the platform; the configured broker controls the white-label URL, credentials, instruments, and trading permissions.

## Features

- Broker-domain login, explicit account selection, cookie handling, trading-token renewal, and automatic use of the account's trading API domain.
- Complete effective-instrument list with price precision, volume limits, contract size, currencies, and asset type.
- Batched REST-polled Level 1 quotations with bid, ask, high, low, and change.
- Historical and REST-polled live candles from one minute through monthly intervals.
- Balance, equity, margin, free margin, PnL, open positions, active pending orders, and closed-position executions.
- Market position opening, limit and stop pending orders, pending-order cancellation, and full or partial position closing.
- Absolute stop-loss and take-profit fields.
- Retry of safe reads after reauthentication, throttling, or transient server failures. Order submissions and cancellations are never automatically replayed.

Every request, response, quote, candle, order, and account structure uses a typed DTO. The connector does not use `JObject`, `JArray`, `JToken`, `dynamic`, or dictionary-shaped wire models.

## Configuration

- `Address` - the broker's Match-Trader white-label origin, including `https://`.
- `Login` and `Password` - credentials for that broker domain.
- `BrokerId` - broker identifier required by the login endpoint.
- `AccountId` - trading account ID or UUID. It may be omitted when the login response selects one account.
- `PollingInterval` - minimum spacing between rotating quote, candle, order, and portfolio polling jobs.

## Streaming status

The current Platform API reference published in 2026 documents REST endpoints for market watch, candles, positions, orders, and account data. An older official PDF documented a WebSocket command only for requesting a closed-position snapshot; it did not define a realtime quotation or account-event stream, and the current API exposes closed positions through REST. The connector therefore uses rate-aware REST polling and does not claim an undocumented WebSocket feed.

## Documentation

- [StockSharp Match-Trader connector](https://doc.stocksharp.com/en/topics/api/connectors/forex/matchtrader.html)
- [Official Match-Trader Platform API](https://app.theneo.io/match-trade/platform-api)
- [Official technical documentation index](https://docs.match-trade.com/docs/match-trader-api-documentation/)
- [Login](https://app.theneo.io/match-trade/platform-api/login)
- [Effective instruments](https://app.theneo.io/match-trade/platform-api/data/get-symbols)
- [Candles](https://app.theneo.io/match-trade/platform-api/data/get-candles)
- [Open position](https://app.theneo.io/match-trade/platform-api/position/open-position)
- [Create pending order](https://app.theneo.io/match-trade/platform-api/order/create-pending-order)
