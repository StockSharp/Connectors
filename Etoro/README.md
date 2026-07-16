# StockSharp eToro connector

The connector integrates StockSharp with the official [eToro Public API](https://api-portal.etoro.com/). It uses the REST API for reference data, historical data and trading commands, and the official `wss://ws.etoro.com/ws` endpoint for live prices and private trading notifications.

## Supported functionality

- Instrument search and permanent eToro instrument identifiers.
- Real-time Level1 bid, ask and last-execution prices over WebSocket.
- Historical 1-minute through 1-week candles through REST.
- Demo and real portfolios, cash, open positions and unrealized P&L.
- Unified API v2 market and market-if-touched orders.
- Long and short position opening, partial or full position closing and pending-order cancellation.
- Order snapshots, closed-trade history and private WebSocket order/execution updates.
- Automatic WebSocket authentication, reconnect and subscription restoration.

All REST request bodies, responses and WebSocket messages are represented by typed DTOs. The connector does not use `JObject`, `JArray`, `JToken`, `dynamic` or protocol dictionaries.

## Configuration

1. Verify the eToro account and open **Settings > Trading > API Key Management**.
2. Create a key for either the Demo or Real environment. Enable Write permission when trading is required.
3. Set `PublicApiKey` and `UserKey` on `EtoroMessageAdapter`.
4. Set `IsDemo` to the environment selected when the key was created. eToro keys are bound to one environment.

`EtoroOrderCondition` selects the settlement type, leverage and whether StockSharp volume means asset units, cash amount or contracts. Set `PositionId` to close an existing position; leave it empty to open a new one. A StockSharp limit order maps to eToro's `mit` (market-if-touched) order with `Price` used as the trigger rate. It is not represented as an exchange-resting limit order.

Use `Sell` to close a long position and `Buy` to close a short position. The `TradeId` reported for an execution is the eToro position ID accepted by `EtoroOrderCondition.PositionId`. Short selling and leverage can require the `Cfd` settlement type, depending on the instrument and account region.

## API limitations

- The public WebSocket exposes top-of-book rates, not a multi-level order book. Market depth is therefore not advertised.
- The price stream includes the latest execution price but no execution size or exchange trade identifier, so tick trades are not fabricated.
- The candle endpoint returns at most 1,000 recent candles and has no date cursor. Requested date ranges are filtered within that available window.
- Live candle topics are not documented by eToro; candle subscriptions are historical only.
- Available instruments, settlement types, leverage and real-asset trading depend on the account entity and region.
- Social feeds, copy trading and agent-portfolio endpoints are outside this trading connector.

## Official documentation

- [eToro Developer Portal](https://api-portal.etoro.com/)
- [Authentication and key creation](https://api-portal.etoro.com/getting-started/authentication)
- [Create a real order with Unified API v2](https://api-portal.etoro.com/api-reference/trading--real/create-an-order)
- [Create a demo order with Unified API v2](https://api-portal.etoro.com/api-reference/trading--demo/create-an-order)
- [Find an instrument ID](https://api-portal.etoro.com/guides/get-instrument-id)
- [WebSocket overview](https://api-portal.etoro.com/api-reference/websocket/overview)
- [WebSocket topics](https://api-portal.etoro.com/api-reference/websocket/topics)
- [Rate limits](https://api-portal.etoro.com/getting-started/rate-limits)
- [OpenAPI specification](https://api-portal.etoro.com/api-reference/openapi.json)

eToro and the eToro logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by eToro.
