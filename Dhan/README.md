# DhanHQ v2 connector

This directory contains the DhanHQ API v2 connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Protocol coverage

- DhanHQ v2 REST endpoints for orders, trades, Forever/GTT orders, funds, positions, holdings, historical candles, and the official detailed instrument master.
- Five-level depth from the normal full packet. Requests with `MaxDepth` up to 20 or 200 use the separate official full-depth WebSocket endpoints and require the corresponding Dhan data entitlement.
- The dedicated order-update WebSocket at `wss://api-order-update.dhan.co` for realtime updates to all account orders.
- Intraday candles at 1, 5, 15, 25, and 60 minutes, plus daily candles. Intraday ranges are split into the documented 90-day request windows.

Market-data and full-depth packets are decoded directly from their documented binary layouts.

One normal market-feed connection accepts up to 5,000 instruments. The extended-depth feeds are limited to NSE equity and derivatives: up to 50 instruments for 20-level depth and one instrument for 200-level depth per connection. The connector enforces these limits before sending a subscription.

## Authentication

Configure the Dhan client ID and a current access token generated through Dhan Web. User-generated access tokens expire after 24 hours. Order placement, modification, cancellation, and Forever order operations require the static public IP registered with Dhan.

Trading APIs are available to Dhan clients. Market-data APIs, including 20-level and 200-level depth, may require a separate paid subscription.

## Security identifiers

Run a security lookup before subscribing or trading. The connector stores `exchangeSegment|securityId` in `SecurityId.Native`, preserving segment identity when numeric security IDs overlap.

## Official references

- [DhanHQ API v2](https://dhanhq.co/docs/v2/)
- [Live market feed](https://dhanhq.co/docs/v2/live-market-feed/)
- [Full market depth](https://dhanhq.co/docs/v2/full-market-depth/)
- [Orders](https://dhanhq.co/docs/v2/orders/)
- [Forever orders](https://dhanhq.co/docs/v2/forever/)
- [Official Python SDK](https://github.com/dhan-oss/DhanHQ-py)
