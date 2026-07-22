# StockSharp Groww Trading API connector

This connector integrates StockSharp with the official [Groww Trading API](https://groww.in/trade-api/docs). It uses typed REST DTOs for reference data, history, portfolios and trading, plus Groww's NATS-over-WebSocket feed with the broker's protobuf messages for realtime data and private updates.

## Supported functionality

- Complete Groww instrument CSV lookup for NSE, BSE and MCX equities, indices, ETFs, futures, options and commodity derivatives.
- Realtime LTP, index values and five-level order books for the CASH and FNO subjects published by Groww's NATS feed.
- Realtime equity and FNO order updates and FNO position updates.
- Current `/historical/candles` history for all advertised intervals from one minute through one month, with automatic splitting at Groww's per-request limits.
- Order placement, modification and cancellation for CASH, FNO and COMMODITY segments.
- Daily orders and executions, profile, margins, positions and holdings.
- Access-token, API-key/secret approval and API-key/TOTP authentication flows. TOTP codes are generated locally from the configured Base32 secret.
- Automatic NATS reconnect and restoration of active NATS subscriptions.

Every JSON request and response is represented by a typed DTO. Realtime frames use generated classes from Groww's official protobuf descriptors. The connector does not use `JObject`, `JArray`, `JToken`, `dynamic` or protocol dictionaries.

## Configuration

An active Groww Trading API subscription is required.

Choose one authentication mode:

1. Set `Token` to a token generated in Groww. A manually generated token expires daily at 06:00 IST.
2. Set `Key` and `Secret` for the approval flow. Daily approval in Groww Cloud is still required by Groww.
3. Set `Key` to the TOTP token and `TotpSecret` to its Base32 secret. The connector creates the current six-digit TOTP code locally.

`DefaultProduct` selects the product used when `GrowwOrderCondition.Product` is not specified. The order condition also exposes native validity and stop trigger price.

Select instruments through the connector's security lookup. Groww streaming subjects require the exchange token from the official instrument CSV; the connector preserves it together with exchange, segment and Groww symbol in `SecurityId.Native`.

## Protocol details and limitations

- REST base URL: `https://api.groww.in/v1`.
- Realtime endpoint: `wss://socket-api.groww.in`. Groww first exchanges a generated NATS user public key for a scoped JWT and subscription ID, then authenticates NATS with that JWT and the matching NKey seed.
- Groww permits up to 1000 instrument feed subscriptions. The connector enforces this per adapter instance.
- The documented NATS subjects currently cover NSE/BSE CASH and FNO data, equity/FNO order updates and FNO position updates. MCX trading and portfolio queries work through REST, but Groww's SDK does not publish an MCX NATS subject, so the connector rejects MCX realtime subscriptions explicitly.
- The LTP protobuf contains time and price but no last-trade quantity. StockSharp tick messages therefore omit volume rather than reporting cumulative volume as trade size.
- Backtesting history currently supports CASH and FNO only and is available from 2020 according to Groww. The API limits each request to 30 days for 1/2/3/5-minute bars, 90 days for 10/15/30-minute bars, and 180 days for larger intervals.
- Groww groups rate limits by operation: orders are limited to 10 requests/second and 250/minute, live REST data to 10/second and 300/minute, and non-trading requests to 20/second and 500/minute.
- NATS reconnects reuse the scoped socket JWT. If Groww expires or revokes that credential, reconnect the StockSharp adapter to request a new socket token and private subscription ID.
- Order submission is never automatically repeated after an ambiguous network failure. Retrying it could create a duplicate order; use the generated `SS-...` order reference to reconcile status.

## Official documentation

- [Groww Trading API](https://groww.in/trade-api/docs)
- [Authentication and rate limits](https://groww.in/trade-api/docs/curl)
- [Instrument master CSV](https://groww.in/trade-api/docs/curl/instruments)
- [Orders](https://groww.in/trade-api/docs/curl/orders)
- [Portfolio](https://groww.in/trade-api/docs/curl/portfolio)
- [Historical candles and backtesting](https://groww.in/trade-api/docs/curl/backtesting)
- [Official feed guide](https://groww.in/trade-api/docs/python-sdk/feed)
- [Official Python SDK package](https://pypi.org/project/growwapi/)

Groww and its marks are the property of their respective owner. StockSharp is not affiliated with or endorsed by Groww.
