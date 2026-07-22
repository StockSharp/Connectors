# OpenMarkets Connector for StockSharp

This connector integrates StockSharp with the current OpenMarkets Australia APIs. OpenMarkets is a commercial, API-first execution and market-infrastructure provider; credentials, scopes, market-data entitlements, accounts, and UAT access are issued during onboarding.

## Features

- OAuth 2.0 client-credentials authentication with a separate cached token for each required API scope.
- Production and OpenMarkets UAT/test endpoints.
- Security-master lookup and detailed security metadata.
- REST quote snapshots and real-time quote updates over the official SignalR/MessagePack stream.
- Historical trades over REST and real-time trade updates over SignalR/MessagePack.
- REST market-depth snapshots with rate-aware round-robin polling for active subscriptions.
- Intraday minute and daily/weekly time-series candles.
- Order-account discovery and account-to-portfolio mapping.
- Cash balances, portfolio positions, orders, and executions from the OMS REST API.
- Live orders, executions, portfolio positions, and cash updates over the OMS SignalR/MessagePack stream.
- Market and limit order entry, amendment, and cancellation through OMS v4.
- Day, dated, Fill-and-Kill, and Fill-or-Kill lifetime mapping.

Safe reads may retry transient transport failures, HTTP 429, and server failures. Create, amend, and cancel operations are never automatically retried.

## Configuration

- `Key` and `Secret` - OAuth credentials issued by OpenMarkets.
- `AccountCode` - optional order-account restriction. Leave it empty to expose every account available to the API client. Orders must identify a portfolio when several accounts are exposed.
- `IsTest` - use stage identity plus the test OMS, market-data, and streaming services.
- `DataSource` - IRESS market-data source used in `security.exchange@datasource` identifiers and stream subscriptions. The default is `TM`.
- `DefaultExchange` - fallback exchange when StockSharp's security ID has no board code. The default is `ASX`.
- `DefaultDestination` - OMS destination for new orders. The default is `ASX`; use the value enabled for the account and returned by OpenMarkets' destinations endpoint.
- `OrderGiver` and `OrderTaker` - optional order-origin fields required by some Australian compliance workflows.
- `DefaultPriceMultiplier` - fallback conversion from native IRESS prices to decimal prices. The default is `0.01`; security and quote metadata override it when OpenMarkets supplies `priceMultiplier`.
- `DepthPollingInterval` - minimum spacing between REST depth refreshes. It is clamped to one second and shared round-robin across subscriptions.

OpenMarkets checks scopes independently. REST calls request `oms-api` or `market-data-api`; SignalR connections request `oms-streams-api` or `md-streams-api`. A client plan that does not include a requested scope will be rejected by the OpenMarkets identity service.

## Price and account mapping

IRESS market-data and OMS responses commonly express Australian security prices in cents and return a `priceMultiplier` such as `0.01`. The connector caches that multiplier per security, converts market data and executions to StockSharp decimal prices, and reverses the conversion when placing or amending an order. The configured fallback is used only when security and quote metadata omit the multiplier.

OpenMarkets distinguishes order accounts from holding portfolios. The connector publishes order accounts as StockSharp portfolios and uses the account-to-portfolio links returned by OMS to attach cash and positions to the corresponding order account. If several accounts are available, `PortfolioName` must select the intended account for trading.

## Streaming behavior

OpenMarkets market-data streams subscribe by data source rather than by symbol. The connector establishes one MessagePack SignalR connection, subscribes once for quotes and/or trades, and filters the source-wide updates against active StockSharp subscriptions. It automatically re-subscribes after SignalR reconnects.

The OMS stream is business-wide and supplies the current trading day's order and trade events plus portfolio position and cash updates. The connector filters inaccessible accounts and routes events to the matching StockSharp order-status or portfolio subscription. Historical snapshots remain available through REST.

Market depth has no documented streaming hub method and is therefore refreshed with REST snapshots. Candles are historical REST results; the connector does not claim a live candle stream.

## Trading scope

The standard OMS v4 endpoint is used for market and limit orders. The connector does not silently translate StockSharp orders into OpenMarkets FIXED CO, trailing contingent, OCO, or IF-DONE strategies because those workflows require destination-specific fields and entitlements. Applications needing those strategies should model and validate their native parameters explicitly before extending the adapter.

OpenMarkets says order-stream events cover orders created during the current trading day. The connector consequently loads the recent order snapshot from REST before starting live updates. Individual fill messages use OpenMarkets trade numbers and are not synthesized from cumulative order volume.

## Documentation

- [StockSharp OpenMarkets connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/openmarkets.html)
- [Official OpenMarkets developer portal](https://dev.openmarkets.com.au/)
- [Authentication](https://dev.openmarkets.com.au/docs/authentication)
- [OMS REST API](https://dev.openmarkets.com.au/docs/oms/documentation)
- [OMS streaming API](https://dev.openmarkets.com.au/docs/oms/streaming)
- [Market Data REST API](https://dev.openmarkets.com.au/docs/market-data/documentation)
- [Market Data streaming API](https://dev.openmarkets.com.au/docs/market-data/streaming)
- [OpenMarkets developers page](https://openmarkets.com.au/developers/)
