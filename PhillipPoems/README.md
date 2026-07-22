# Phillip POEMS Connector for StockSharp

This connector integrates StockSharp with the official Singapore POEMS API Gateway and the POEMS Mobile 2.0 stock API exposed through it.

## Features

- OAuth 2.0 bearer authentication through the POEMS API Gateway, including refresh-token renewal.
- Official production and sandbox gateway endpoints.
- Global stock and ETF counter search across POEMS-supported exchanges.
- Level1 quote snapshots with rate-aware REST polling.
- Current-day time-and-sales history and live polling with duplicate suppression.
- Entitlement-aware market-depth snapshots with REST polling.
- Stock account balances and holdings grouped by native currency.
- Current-day order status and aggregate execution updates.
- Limit, stop-limit, and limit-if-touched stock orders using the required validate-then-submit workflow.
- Quantity amendment and order withdrawal through the documented stock endpoints.
- Cash, CPF, and SRS payment modes plus explicit settlement currency.

## Configuration

- `Key` and `Secret` - OAuth application credentials registered with POEMS. They are required for automatic token refresh.
- `ApiKey` - the `X-Api-Key` issued for the POEMS application.
- `Token` - bearer token obtained through the authorization-code flow.
- `RefreshToken` - optional refresh token used to renew the access token without another browser login.
- `AccountNo` - POEMS account number and StockSharp portfolio name. It must match the OAuth session.
- `AccountType` - native stock account type used by portfolio endpoints; the default is `V`.
- `EncryptedPin` - optional session-specific value produced by Phillip's E2EE client library when the account requires transaction re-authentication.
- `IsDemo` - use `https://sandboxapi.poems.com.sg/api-gateway/pspl/`; otherwise use the production gateway at `https://api.poems.com.sg/api-gateway/pspl/`.
- `DefaultMarket`, `DefaultExchange`, and `DefaultSettlementCurrency` - fallbacks for manually constructed security and order messages.
- `PollingInterval` - minimum spacing between round-robin quote, trade, depth, order, and portfolio refreshes.

The initial access and refresh tokens are obtained with the documented three-legged authorization-code flow. The connector deliberately does not accept a POEMS password and does not reproduce the proprietary E2EE login library.

## Market data behavior

The public third-party specification documents REST snapshots and proprietary PMP topic metadata, but it does not publish a WebSocket or PMP wire protocol. Consequently, this connector uses REST polling and does not claim streaming support. Level1 subscriptions are batched. Time-and-sales subscriptions are limited to the current exchange day because the stock endpoint exposes times rather than historical trade dates. Stock candles are not advertised because the documented chart endpoint only returns proprietary chart-service connection metadata.

Market depth requires a separate exchange entitlement. POEMS returns a normal API error when the account is not subscribed. Availability and maximum depth vary by exchange.

## Trading behavior

POEMS requires every new stock order to be validated first. The returned one-use `authToken` is then supplied to the submit endpoint. Create, amend, and withdrawal requests are never retried after ambiguous transport failures. Read-only requests may retry transient transport errors, rate limits, and server failures.

The public stock API documents quantity-only amendment. A replacement that changes price is rejected locally instead of being silently converted into cancel/re-register. Order polling reports native order state and emits aggregate fill deltas because the public response exposes cumulative executed quantity and price rather than individual execution identifiers.

## Documentation

- [StockSharp Phillip POEMS connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/phillip_poems.html)
- [Official POEMS API portal](https://www.poems.com.sg/docs/poemsapi/)
- [POEMS API Gateway and OAuth reference](https://www.poems.com.sg/docs/poemsapi/home/)
- [POEMS Mobile 2.0 Global API](https://www.poems.com.sg/docs/poemsapi/pmobile2/global/)
- [POEMS Mobile 2.0 Stocks API](https://www.poems.com.sg/docs/poemsapi/pmobile2/st/index.html)
