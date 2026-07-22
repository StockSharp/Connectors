# BMLL connector for StockSharp

This connector integrates the official BMLL Data API for licensed historical market
data. It authenticates either with the RSA private key registered in BMLL Data Lab or
with an already issued bearer token, submits asynchronous dataset queries, polls their
status, and streams the returned gzip JSON-lines file into StockSharp messages.

## Supported functionality

- historical trades as `DataType.Ticks`;
- historical Level 3 messages as `DataType.OrderLog`;
- market-depth snapshots reconstructed from BMLL Level 3 order-by-order events;
- ticker, exchange ticker, and optional MIC filtering;
- nanosecond Unix timestamps and documented textual timestamp formats;
- dataset entitlement discovery during connection;
- bounded retries for rate limits and transient server failures;
- streaming decompression and JSON-lines parsing for large result files.

BMLL data is historical and delivered on a T+1 basis. The documented Data API does
not provide a live WebSocket feed, so this connector deliberately exposes history
only and does not simulate streaming by repeatedly downloading completed datasets.

## Authentication

The default `SshKey` mode follows the BMLL Data SDK authentication sequence:

1. Set `Login` to the BMLL Data Lab user name.
2. Set `PrivateKeyPath` to the PEM-encoded RSA private key whose public key is
   registered with BMLL.
3. Set `Password` only when that private key is encrypted.
4. The connector obtains a session ID, signs the documented RS256 claims, exchanges
   them for a bearer token, and renews it when required.

For `BearerToken` mode, set `Token` to an issued bearer token and, when supplied by
BMLL, set `ApiKey`. Secrets and authenticated download query strings are redacted
from connector errors.

`Address` defaults to `https://api.data.bmlltech.com/` and
`AuthenticationAddress` defaults to `https://auth.data.bmlltech.com/`. Both settings
must be absolute HTTPS addresses.

## Dataset configuration

Dataset identifiers depend on the customer's BMLL entitlements. `TradesDataset`
defaults to `trades`, while `Level3Dataset` defaults to `l3`; replace either value
with the identifier returned by the account's `/datasets` endpoint when the licensed
catalog uses another name. The connector validates configured identifiers against
that catalog before issuing a subscription query.

Set `SecurityId.SecurityCode` to the ticker. For Level 3, it is sent as
`EXCHANGE_TICKER`; for trades, as `TICKER`. Set `SecurityId.BoardCode` to a MIC when
venue filtering is required, or use `BMLL` to leave the MIC unrestricted.

The BMLL Level 3 schema identifies order additions and deletions by `AddOrderIndex`
and `DelOrderIndex`. The connector applies those operations in sequence, resets the
book at each trade date, and emits an aggregated depth snapshot at `EndOfEvent`.

## Official documentation

- [BMLL Data Feed](https://www.bmlltech.com/products/bmll-data-feed)
- [BMLL Data Feed documentation and datasets](https://www.bmlltech.com/products/bmll-data-feed/documentation)
- [BMLL Data SDK guide](https://www.bmlltech.com/files/documents/BMLL-Data-SDK.pdf)
- [BMLL Level 3 schema](https://www.bmlltech.com/files/documents/BMLL-L3-schema.pdf)
- [BMLL normalised trades schema](https://www.bmlltech.com/files/documents/BMLL-Normalised-Trades.pdf)
- [StockSharp BMLL connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/bmll.html)

BMLL and its marks are trademarks of BMLL Technologies Ltd. StockSharp is not
affiliated with or endorsed by BMLL.
