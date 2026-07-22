# FYERS API v3 connector

This directory contains the FYERS API v3 connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Protocol coverage

- API v3 REST endpoints for profile, funds, positions, holdings, orders, trades, GTT/OCO orders, historical candles, and the official daily symbol-master files.
- The current HSM market-data WebSocket at `wss://socket.fyers.in/hsm/v1-5/prod`, including direct decoding of the documented binary snapshot, update, lite, acknowledgement, and heartbeat packets.
- Realtime Level 1, trades, and five-level market depth through the HSM stream. The connector restores active subscriptions after reconnect and enforces the current 5,000-topic limit.
- The separate TBT WebSocket discovered through the FYERS API, with direct Protobuf decoding of 50-level depth updates.
- The order WebSocket at `wss://socket.fyers.in/trade/v3` for realtime order, trade, and position updates.
- Historical candles at 1, 2, 3, 5, 10, 15, 20, 30, 60, 120, and 240 minutes, plus daily candles. Long ranges are split into API-sized request windows.

## Authentication and current trading rules

Configure the App ID in `ClientId` and a current access token in `Token`. FYERS API access is free for FYERS clients. Availability of individual exchanges and the TBT depth stream depends on the permissions and market-data entitlements of the account.

Effective April 1, 2026, order requests require an activated compliant app whose App ID ends in `200` and a whitelisted static IP. FYERS also requires daily 2FA, limits API order flow to 10 orders per second, and converts market orders to Market Price Protection orders. Older apps remain data-only and cannot place orders.

## Security identifiers

Run a security lookup before subscribing or trading. The connector stores the FYERS symbol and native `fyToken` in `SecurityId.Native`; the token is required to build HSM subscription topics without a separate symbol-conversion payload.

## Official references

- [FYERS API v3 documentation and dashboard](https://myapi.fyers.in/docsv3)
- [FYERS API product page](https://fyers.in/products/api)
- [Official API v3 sample repository](https://github.com/FyersDev/fyers-api-sample-code/tree/sample_v3)
- [Official Python SDK](https://pypi.org/project/fyers-apiv3/)
- [April 2026 retail-algo requirements](https://support.fyers.in/portal/en/kb/articles/what-are-the-new-sebi-rules-for-retail-algo-trading-from-april-01-2026)
- [Compliant app activation](https://support.fyers.in/portal/en/kb/articles/how-do-i-activate-the-new-app-for-api-trading-after-april-1-2026)
