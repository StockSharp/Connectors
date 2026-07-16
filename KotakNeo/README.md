# StockSharp Kotak Neo Trade API v2 connector

This connector integrates StockSharp with the production Kotak Neo Trade API v2. It uses the official two-step TOTP/MPIN login, the `baseUrl` returned by MPIN validation, the binary HSM market-data WebSocket, and the separate order-feed WebSocket selected from the authenticated account's data center.

## Configuration

- `ConsumerKey` is the consumer token generated in the Kotak Neo app or web portal.
- `MobileNumber` includes the country code, for example `+91...`.
- `UserCode` is the account UCC shown in the Kotak Neo profile.
- `Mpin` is the account MPIN.
- `TotpSecret` is the Base32 secret registered with an authenticator application. The connector generates the current six-digit TOTP locally; it does not send the secret itself to Kotak.
- `DefaultProduct` is used when `KotakNeoOrderCondition.Product` is not specified.

Treat the consumer token, MPIN, session tokens, and TOTP secret as credentials. Do not write them to logs or source control.

## Supported operations

- instrument lookup from all scrip-master CSV files returned by the authenticated v2 endpoint;
- realtime Level1, last trades, and five-level market depth;
- native index, stock, derivatives, currency, and commodity HSM topics;
- market, limit, stop-limit, and stop-market orders;
- DAY and IOC validity, AMO, disclosed quantity, market protection, CNC/MIS/NRML/CO/BO/MTF products, and bracket-order fields exposed through `KotakNeoOrderCondition`;
- order replacement and cancellation;
- order and trade recovery through REST plus live order updates through the order-feed WebSocket;
- limits, positions, and holdings.

Kotak Neo v2 does not expose a historical candle endpoint in the current official SDK, so this connector does not advertise history or candle support. Market depth is not available for index topics.

## Protocol details and limits

TOTP login and MPIN validation use the fixed `mis.kotaksecurities.com` endpoints. MPIN validation returns the trade token, SID, HSM server identifier, data center, and a dynamic `baseUrl`; all subsequent REST calls use that returned URL.

Market data uses `wss://mlhsm.kotaksecurities.com` with the official binary HSM framing. The connector maintains separate `sf`, `if`, and `dp` subscriptions, distributes them across channels 2 through 16, limits a channel to 200 topics, limits a request to 100 topics, sends required acknowledgements, and restores subscriptions after reconnect. The documented SDK permits 3000 live subscriptions per connection.

Order updates use a separate JSON WebSocket. Its host is selected from `dataCenter` (`gdc`, `adc`, `e21`, `e22`, `e41`, or `e43`) and receives a heartbeat every 25 seconds.

Kotak's public page currently contains differing marketing and FAQ order-rate figures. The connector therefore does not assume a fixed client-side rate and leaves enforcement to the current API response and broker policy. Observe HTTP 429 responses and the account's current exchange/static-IP requirements.

## Official references

- [Kotak Neo Trade API](https://www.kotaksecurities.com/platform/kotak-neo-trade-api-v1/)
- [Official Kotak Neo API v2 Python SDK](https://github.com/Kotak-Neo/Kotak-neo-api-v2)
- [API v2 migration guide](https://www.kotaksecurities.com/uploads/API_Migration_guide_03_12_2025_accfccef45.pdf)
- [Kotak Neo API portal](https://napi.kotaksecurities.com/devportal/)

The wire models and HSM framing in this connector follow the official v2 SDK. Verify broker announcements and exchange rules before production deployment.
