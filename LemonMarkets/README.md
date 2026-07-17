# lemon.markets Connector for StockSharp

This connector integrates StockSharp with the current lemon.markets Brokerage API. The API is a Brokerage-as-a-Service product for approved partners and is invite-only; it is not the discontinued retail Trading API.

## Features

- Customer-account and securities-account discovery with cursor pagination.
- Instrument discovery for stocks, ETFs, and funds, identified by ISIN.
- Latest quote or NAV retrieval through the official instrument-prices endpoint.
- Cash balance, buying power, blocked cash, withdrawable funds, and open positions.
- Market-order creation, confirmation, cancellation, order history, and exact trade reports.
- Idempotency keys on order creation so a safe transport retry cannot create a duplicate order.
- Efficient order and trade lifecycle polling through the official events endpoint.
- Sandbox and production Brokerage API environments.

All JSON request and response bodies use typed DTOs. Safe GET requests and idempotent order creation can be retried after transient transport, HTTP 429, and server failures. Confirmation and cancellation requests are not automatically retried because the public documentation does not declare those operations idempotent.

## Configuration

- `ApiKey` — Bearer key issued by lemon.markets for the Brokerage API.
- `IsDemo` — selects `https://sandbox.api.lemon.markets/v1`; clear it to use `https://api.lemon.markets/v1`.
- `AccountId` — customer-account ID. It can be omitted only when the API key exposes exactly one usable account.
- `SecuritiesAccountId` — default securities-account ID. It is required for order routing when a customer has multiple securities accounts.
- `DataPrivacyPrincipal` — the real customer or service principal sent in `LMG-Data-Privacy-Access-Principal`.
- `DataPrivacyJustification` — the reason sent in `LMG-Data-Privacy-Access-Justification`.
- `PersonId` — optional person ID recorded as the actor for order actions.
- `DefaultFeeAmount` — fixed partner fee in EUR. The API requires a fee object even when the fee is zero.
- `IsAppropriatenessConsentAccepted` — permits automatic confirmation only after the customer has provided the required acknowledgement.
- `PollingInterval` — latest-price, event, balance, and position polling period; the minimum is five seconds.

The two data-protection headers are mandatory in the current API. Configure meaningful values that follow the lemon.markets logging guidance rather than placeholders.

## Order mapping

The current Brokerage API accepts amount-based buys and quantity-based sells. Consequently, `OrderRegisterMessage.Volume` maps to the EUR cash amount for a buy and to the number of shares for a sell. The same native value is reported as `ExecutionMessage.OrderVolume`; executions always report their actual share quantity in `TradeVolume`.

Only `OrderTypes.Market` is advertised. Limit, stop, time-in-force, expiry, replace, and group-cancel operations are not exposed by the current public contract.

`LemonMarketsOrderCondition` can override the fixed or percentage partner fee, the securities-account ID, and appropriateness consent for one order. Fixed and percentage fees are mutually exclusive. Trading halts returned by the instrument endpoint are checked before submitting a matching buy or sell.

Order creation and confirmation are separate API operations. If the returned order requires Strong Customer Authentication, the connector reports the created native order ID and stops before confirmation; the customer challenge must be completed through the partner's SCA flow. The connector never fabricates an SCA signature or silently accepts an appropriateness acknowledgement.

## Market-data scope

The Brokerage API exposes the latest quote or fund NAV per instrument, not a WebSocket feed. Level 1 subscriptions are therefore REST-polled. The connector does not claim ticks, order books, candles, or streaming quotes. A quote maps to best bid/ask price and size; a NAV maps to the latest price with its valuation date.

## Documentation

- [StockSharp lemon.markets connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/lemon_markets.html)
- [Official lemon.markets Brokerage API developer hub](https://developer.lemon.markets/)
- [Official authorization and data-protection headers](https://developer.lemon.markets/docs/authorization)
- [Official number formats](https://developer.lemon.markets/docs/fundamental-number-formats)
- [Official instrument list](https://developer.lemon.markets/reference/list_instruments-1)
- [Official prices endpoint](https://developer.lemon.markets/reference/get_prices-1)
- [Official order creation](https://developer.lemon.markets/reference/create_order-1)
- [Official order confirmation](https://developer.lemon.markets/reference/confirm_order-1)
- [Official trades endpoint](https://developer.lemon.markets/reference/list_trades-1)
- [Official events endpoint](https://developer.lemon.markets/reference/list_events-1)
- [Official idempotency guide](https://developer.lemon.markets/docs/idempotency)
