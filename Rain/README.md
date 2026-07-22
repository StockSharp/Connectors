# Rain Connector

The connector integrates Rain Pro spot markets through Rain's current production
REST and WebSocket interfaces in Bahrain.

Supported features:

- discovery of the available spot products, including native price, quantity,
  and minimum-order increments;
- REST Level1 snapshots and OHLCV candle history for every interval currently
  exposed by Rain Pro;
- realtime Level1, public trades, sequence-checked incremental order books, and
  native candle updates through the official Rain Pro WebSocket;
- automatic WebSocket reconnect, subscription restoration, heartbeat, and a new
  order-book snapshot after a sequence gap;
- account balances and realtime private order updates;
- closed-order history, individual order lookup, embedded account trades, limit
  and market orders, individual cancellation, and cancellation of locally
  tracked open orders;
- bounded retry of safe reads, rate-conscious requests, API errors, and an
  8 MiB response safety limit.

Public market data does not require credentials. Private operations require an
API key, API secret, and account access token issued for the relevant Rain
account or institutional integration. Signed requests use Rain's current
`api-key`, `api-content-hash`, `api-timestamp`, and `api-signature` headers plus
the bearer access token. A market-buy quantity is a quote-currency amount; it can
be supplied explicitly through `RainOrderCondition.QuoteAmount`.

Rain currently exposes its production interface through Rain Pro but does not
publish a standalone public API reference. This implementation follows the
current production contracts used by Rain Pro. Endpoint overrides are available
for credentials assigned to a different Rain environment.

Official resources:

- [Rain Pro](https://www.rain.com/en/trade)
- [Rain Pro guide](https://www.rain.com/en/support/articles/10757120-rain-pro)
- [Rain Pro features](https://www.rain.com/en/support/articles/10757125-rain-pro-features)
- [Rain Pro trading rules](https://www.rain.com/legal/rain-pro-trading-rules)
- [Rain website](https://www.rain.com/)
