# StockSharp Zero Hash Connector

The connector integrates StockSharp with the Zero Hash central limit order
book (CLOB). It uses the authenticated CLOB REST API for instruments,
balances, order entry, cancellation, and history, plus the long-lived CLOB
HTTP subscriptions for real-time order books and private order events.

## Access and configuration

Zero Hash API access is provisioned to approved platform clients. Configure
the API key, base64-encoded secret, and passphrase supplied by Zero Hash. For
private trading, also configure:

- `Account`: the fully-qualified CLOB account, for example
  `firms/F-PLAT01/accounts/CUST01-label`;
- `User`: the case-sensitive fully-qualified CLOB user, for example
  `firms/F-PLAT01/users/CUST01`.

The production API default is `https://api.zerohash.com/`. Certification can
be selected by entering `https://api.cert.zerohash.com/` in `ApiEndpoint`.
Both environments use the same signed-header scheme. Secrets are persisted
through StockSharp secure-string storage and are never included in diagnostic
output.

Zero Hash signs the exact minified request body with HMAC-SHA256. The
connector base64-decodes the configured secret, includes the complete request
route in the signature, and sends the required `X-SCX-*` headers. Prices and
quantities are converted with each instrument's published `price_scale` and
`fractional_qty_scale`; no fixed crypto precision is assumed.

## Protocol coverage

The connector supports:

- paginated CLOB instrument discovery and security lookup;
- real-time ticks, Level 1, and aggregated market depth;
- portfolio and per-asset balance snapshots with periodic reconciliation;
- market-to-limit, limit, stop, and stop-limit order entry;
- GTC, IOC, FOK, and good-till-time instructions;
- post-only, all-or-none, self-match prevention, regulatory order capacity,
  strict-limit, best-limit, and stop-trigger parameters;
- single and filtered group cancellation;
- live private order and execution updates;
- paginated order and fill history.

The CLOB market-data and order subscriptions are streaming HTTP endpoints,
not ordinary WebSockets. The separate Zero Hash private WebSocket publishes
RFQ prices and balances for other product flows; it does not provide the CLOB
book and is deliberately not substituted for the CLOB streams here.

The documented REST order search retains up to 14 days. Cancel/replace is
available through the Zero Hash FIX interface but is not exposed by the
documented CLOB REST API, so this adapter reports replace requests as
unsupported. Custody, deposits, withdrawals, settlement webhooks, RFQ trades,
and FIX sessions are outside this connector.

Nanosecond API timestamps are normalized to UTC `DateTime` values.

## Official resources

- [Zero Hash developer documentation](https://docs.zerohash.com/)
- [API authentication](https://docs.zerohash.com/reference/api-authentication)
- [CLOB API overview](https://docs.zerohash.com/reference/clob)
- [CLOB market-data subscription](https://docs.zerohash.com/reference/post_orders-v1-create-market-data-subscription)
- [CLOB order subscription](https://docs.zerohash.com/reference/post_orders-v1-create-order-subscription)
