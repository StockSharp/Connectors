# Coinhako Connector

The connector integrates Coinhako through the official Public API v1.

Supported features:

- discovery of Coinhako indicative spot markets for configurable counter
  currencies;
- live Level1 bid and ask prices through rate-conscious REST polling;
- account balances, including separately reported order and alternative-product
  locks;
- RFQ market orders and limit orders, client order IDs, GTC and GTD expiry,
  individual cancellation, and filtered group cancellation;
- current and historical order lookup with live status polling;
- ECDSA secp256k1 request signing, typed error handling, bounded retries for
  safe reads, and lost-placement/cancellation-response reconciliation.

Public spot prices do not require credentials. Account and trading operations
require Coinhako beta API access: `Key` is the hexadecimal public key sent as
`X-API-KEY`, while `Secret` is the PEM-encoded ECDSA secp256k1 private key. The
signature covers the HTTP method, absolute target URI, SHA-256 body digest,
timestamp, and algorithm exactly as specified by Coinhako.

Coinhako's current Public API v1 does not publish a WebSocket or an order-book,
trade, or candle feed. Consequently, the connector polls the official spot-price
endpoint for Level1 and does not advertise unsupported market-depth, tick, or
candle capabilities. The API also reports completed orders but no individual
execution records; the adapter emits one deterministic completion execution for
a completed order and clearly identifies it by the order ID.

Every request and response payload is represented by a concrete DTO. The
transport does not use loose JSON trees, anonymous protocol objects, or protocol
dictionaries.

Official resources:

- [Coinhako Public API documentation](https://www.coinhako.com/api-docs/index.html)
- [Coinhako API terms](https://www.coinhako.com/legal)
- [Coinhako website](https://www.coinhako.com/)
