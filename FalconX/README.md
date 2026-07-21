# StockSharp FalconX Connector

The connector integrates StockSharp with FalconX institutional spot trading.
It uses the FalconX REST API for reference data, balances, FOK orders, and
order history, the price WebSocket for executable quotes, and the order
WebSocket for persistent and TWAP orders.

## Access and configuration

FalconX API access is provisioned to approved clients. Configure the API key,
base64-encoded secret, and passphrase supplied by FalconX. The production
defaults are:

- REST: `https://api.falconx.io/`;
- executable-price stream: `wss://stream.falconx.io/price.tickers`;
- order stream: `wss://order.falconx.io/order`.

FalconX may restrict credentials by source IP and entitlements. Sandbox or
client-specific endpoints can be entered explicitly without changing the
connector. Secrets are persisted through StockSharp secure-string storage and
are never included in diagnostic output.

`QuoteLevels` contains the positive base-asset quantities requested from the
price stream. FalconX returns an executable two-sided price for each quantity.
The adapter exposes `sell_price` as the bid and `buy_price` as the ask, and
constructs market-depth levels from the incremental quantity curve. These are
executable RFQ/RFS prices, not a public exchange order book.

## Protocol coverage

The connector supports:

- enabled token-pair discovery and security lookup;
- real-time Level 1 and synthetic market depth from executable-price updates;
- portfolio and asset-balance snapshots;
- REST market and limit FOK orders;
- WebSocket limit GTC/GTX and market or limit TWAP orders;
- order cancel, replace, live status updates, fills, and REST history.

REST history is requested in windows of at most 31 days and in pages of at
most 100 orders, matching the documented API limits. FalconX does not expose
public trades or candles through these APIs, so those data types are not
advertised. Custody, transfers, derivatives, allocations, and undocumented
client-specific algorithms are outside this adapter.

All JSON request and response bodies use explicit protocol DTOs. The connector
does not use dynamic JSON, JSON trees, anonymous protocol objects, protocol
dictionaries, or untyped object arrays. FalconX timestamps are normalized to
UTC `DateTime` values.

## Official resources

- [FalconX API documentation](https://app.falconx.io/docs/rfq-rfs?token=3626)
- [FalconX WebSocket examples](https://github.com/falconxio/fx-ws-clients)
- [FalconX electronic trading](https://www.falconx.io/services/trading)
