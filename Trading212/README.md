# Trading 212 Connector for StockSharp

This connector integrates StockSharp with the official Trading 212 Public API v0. It uses the documented REST endpoints for Invest and Stocks ISA accounts and supports both the demo and live environments.

## Features

- Instrument discovery from the official instruments and exchanges metadata endpoints, including ticker, ISIN, currency, asset type, and exchange class.
- Account summary, cash availability, reserved cash, realized and unrealized P/L, and open positions.
- Market, limit, stop, and stop-limit orders with fractional quantities where the instrument permits them.
- DAY and GOOD_TILL_CANCEL validity for limit and stop orders.
- Optional extended-hours execution on market orders when Trading 212 marks the instrument as eligible.
- Pending-order polling and exact execution reports from the cursor-based order-history endpoint.
- Cancellation of pending orders and automatic lifecycle polling for orders submitted through the adapter.
- Per-endpoint pacing for the published API limits and rate-limit-aware retry of safe GET requests.

All JSON request and response payloads are represented by typed DTOs. Trading POST requests are never retried automatically because Trading 212 documents the order endpoints as non-idempotent.

## Configuration

- `ApiKey` — the key generated in Trading 212 account settings.
- `ApiSecret` — the secret paired with that key. Authentication uses HTTP Basic with `key:secret`.
- `IsDemo` — selects `https://demo.trading212.com`; clear it to use `https://live.trading212.com`.
- `PollingInterval` — polling period for balances, positions, pending orders, and fills. Values below 10 seconds are clamped to 10 seconds because order history is limited to six requests per minute.

Create the key with the permissions required for account summary, metadata, portfolio, order read/execute, and order history. Test order routing in the demo environment before enabling live trading.

`Trading212OrderCondition.StopPrice` selects stop behavior for `OrderTypes.Conditional`: a zero order price creates a stop-market order, while a positive order price creates a stop-limit order. `IsExtendedHours` applies only to market orders. A null StockSharp `TillDate` maps to GOOD_TILL_CANCEL; a current-day value maps to DAY.

## Scope and limitations

The Public API is currently beta and is available only for Invest and Stocks ISA accounts. Trading 212 states that orders execute only in the account's primary currency and that multi-currency account behavior is not exposed through this API.

Trading 212 does not publish a WebSocket or other streaming market-data interface in the Public API. This connector therefore does not claim realtime quotes, ticks, order books, or candles. Orders, fills, balances, and positions are REST-polled at the configured interval. Order-status lookups return up to 50 recent historical orders by default; set `OrderStatusMessage.Count` to request additional cursor pages while observing the API's history rate limit.

There is no native order-replace endpoint. Replace is deliberately not advertised; cancel the old order and submit a new order as two explicit operations if that workflow is acceptable. A successful cancellation response means that the request was accepted, not that cancellation is guaranteed before an in-flight fill.

## Documentation

- [StockSharp Trading 212 connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/trading212.html)
- [Official Trading 212 Public API](https://docs.trading212.com/api)
- [Official authentication and rate-limit guide](https://docs.trading212.com/api/section/authentication)
- [Official instrument metadata endpoints](https://docs.trading212.com/api/instruments)
- [Official order endpoints](https://docs.trading212.com/api/orders)
- [Official positions endpoint](https://docs.trading212.com/api/positions)
- [Official OpenAPI description](https://docs.trading212.com/_bundle/api.yaml)
- [Trading 212 API Terms](https://www.trading212.com/legal-documentation/API-Terms_EN.pdf)
