# StockSharp Chainlink Data Streams Connector

The connector integrates StockSharp with the current Chainlink Data Streams
REST and WebSocket APIs. It is a read-only market-data connector: it consumes
signed reports but does not verify them onchain, submit transactions, or place
orders.

## Access and endpoints

Set `Key` and `Secret` to the API key and API secret issued for a Chainlink Data
Streams account. Every REST request and WebSocket handshake is authenticated
with the three official headers. The HMAC-SHA256 input contains the HTTP method,
the exact path and query, the SHA-256 body hash, the API key, and the current UTC
timestamp in milliseconds. Credentials are never placed in a URL or diagnostic
message.

The production defaults are:

- `https://api.dataengine.chain.link/` for the REST API;
- `wss://ws.dataengine.chain.link/` for the WebSocket API.

The connector uses `/api/v1/feeds`, `/api/v1/reports/latest`, and
`/api/v1/reports/page`. Live subscriptions use `/api/v2/ws`, matching the
current official SDK. With `IsHighAvailability` enabled, the connector requests
the `X-Cll-Available-Origins` list, connects to every advertised origin, and
deduplicates identical signed reports. If origin discovery is unavailable, it
continues through the primary WebSocket endpoint. Each connection has bounded
reconnect attempts and message sizes.

## Securities and report schemas

The authenticated `/feeds` response intentionally contains feed IDs, not
human-readable symbols or asset metadata. Consequently, the complete 32-byte
feed ID is preserved as both the native identity and the StockSharp security
code. The connector does not invent ticker aliases or infer a quote currency.
Security lookup can filter by feed ID, schema number, or schema name.

The schema version and timestamp resolution are encoded in the feed ID. The
connector validates and decodes every schema supported by the current Chainlink
Data Streams SDK, versions 1 through 13:

- legacy crypto/basic/status, rate, and multi-value reports (v1-v6);
- redemption rates (v7), RWA Standard (v8), and SmartData (v9);
- tokenized assets (v10), RWA Advanced (v11), projected NAV (v12), and
  best-price reports (v13).

Feed identity, exact body length, integer width, timestamp resolution, wrapper timestamps, market-status
range, and signed `int192` range are checked. Price and decimal-volume values
use the Chainlink 18-decimal scale and are range-checked before conversion to
`decimal`.

## Market data

Level 1 subscriptions start with the latest signed report and continue over an
authenticated WebSocket. Historical Level 1 requests page sequential reports
from the REST API. `HistoryLimit`, `HistoryLookback`, and `ReportsPerPage` bound
downloads.

The primary schema value maps to `LastTradePrice` when StockSharp has no more
specific oracle-value field. RWA Advanced reports keep the consensus mid in
`TheorPrice` and map their explicit last-traded price separately. Bid, ask,
top-of-book volumes, value timestamps, and market state are emitted only where
the report schema defines them. Carried RWA timestamps are not treated as
market-open signals; schema market status is authoritative. SmartData ripcord
values stop the security state. Onchain verification fees, extra multi-value
slots, AUM, corporate-action multipliers, and tokenized reference values are
validated but are not relabeled as unrelated StockSharp fields.

Response sizes and UTF-8 are validated, all timestamps become UTC `DateTime`, and API credentials are redacted from errors.

## Official documentation

- [Data Streams overview](https://docs.chain.link/data-streams)
- [REST API](https://docs.chain.link/data-streams/reference/data-streams-api/interface-api)
- [WebSocket API](https://docs.chain.link/data-streams/reference/data-streams-api/interface-ws)
- [Authentication](https://docs.chain.link/data-streams/reference/data-streams-api/authentication)
- [Report schema overview](https://docs.chain.link/data-streams/reference/report-schema-overview)
- [Official Data Streams SDK](https://github.com/smartcontractkit/data-streams-sdk)
- [Release notes](https://docs.chain.link/data-streams/release-notes)
