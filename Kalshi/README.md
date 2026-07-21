# Kalshi connector for StockSharp

The connector integrates Kalshi event contracts through the current Trade API
V2 REST and WebSocket interfaces. It uses the dedicated production and demo API
hosts introduced by Kalshi for external API traders.

Supported functionality:

- discovery of open binary and scalar event-contract markets;
- current Level1 data and authenticated realtime ticker updates;
- REST order-book snapshots and sequence-checked WebSocket depth updates;
- public historical trades and authenticated realtime trade streaming;
- historical 1, 5, 15 and 30 minute, 1 and 4 hour, and daily candles;
- account balance, subaccount positions, orders, fills, and realtime private
  updates;
- V2 limit, GTD, IOC, FOK, post-only, reduce-only, and marketable orders;
- atomic V2 order amendment, individual cancellation, and filtered bulk
  cancellation;
- production and demo environments and Kalshi subaccounts 0 through 63.

Public REST market data does not require credentials. Kalshi requires an
authenticated WebSocket handshake even for public streaming channels, so live
ticker, depth, and trade subscriptions require an API key ID and its matching
PEM-encoded RSA private key. The private key signs requests locally with
RSA-PSS/SHA-256 and is never transmitted.

Each Kalshi market is exposed as one StockSharp binary-option security quoted
in USD from the YES side. A buy is a YES bid; a sell is a YES ask and therefore
represents NO exposure. REST order books contain YES and NO bids, so the
connector converts NO prices into YES asks. WebSocket order-book subscriptions
request `use_yes_price=true` and consume the unified YES-leg price scale.

Kalshi V2 requires a limit price for every order. A StockSharp market order is
therefore submitted as IOC at the worst price needed to fill the requested
volume in the current book; `MatchOrCancel` uses FOK. Order volumes use Kalshi's
fixed-point contract units with 0.01-contract granularity. Prices are validated
against each market's current `price_ranges` rather than the deprecated global
cent tick.

All protocol payloads are represented by concrete DTO classes, including the
two-element price-level wire format. No JSON object tree or protocol map is
used.

Official resources:

- [Kalshi API documentation](https://docs.kalshi.com/)
- [API environments and endpoints](https://docs.kalshi.com/getting_started/api_environments)
- [API keys and RSA-PSS signing](https://docs.kalshi.com/getting_started/api_keys)
- [Market data quickstart](https://docs.kalshi.com/getting_started/quick_start_market_data)
- [Order direction and YES-leg pricing](https://docs.kalshi.com/getting_started/order_direction)
- [WebSocket quickstart](https://docs.kalshi.com/getting_started/quick_start_websockets)
- [Create order V2](https://docs.kalshi.com/api-reference/orders/create-order-v2)
- [Amend order V2](https://docs.kalshi.com/api-reference/orders/amend-order-v2)
- [REST OpenAPI specification](https://docs.kalshi.com/openapi.yaml)
- [WebSocket AsyncAPI specification](https://docs.kalshi.com/asyncapi.yaml)
- [StockSharp Kalshi connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/kalshi.html)

Kalshi and its mark are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Kalshi.
