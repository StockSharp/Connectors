# Flattrade Pi API connector

This directory contains the Flattrade Pi API connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Protocol coverage

- Current JSON REST base URL `https://piconnect.flattrade.in/PiConnectAPI/` for orders, trades, positions, holdings, limits, and historical candles.
- Current WebSocket endpoint `wss://piconnect.flattrade.in/PiConnectWSAPI/` with the `t=a` / `accesstoken` login introduced by Flattrade in 2026.
- Touchline, ticks, and five-level market depth with automatic subscription recovery after reconnect.
- Realtime order, fill, and position updates on the official WebSocket.
- Official NSE, NFO, CDS, MCX, BSE, and BFO scrip-master CSV files.
- One-minute through four-hour intraday candles and daily candles exposed by Pi.

## Authentication

Configure the Flattrade user ID, optional account ID, and current `jKey` access token. When account ID is empty, user ID is used. Generate the token through Flattrade's browser authorization and request-code exchange; the token normally lasts for the trading day and is cleared during the broker's morning maintenance window. The connector deliberately does not store the API secret, trading password, PAN, or date of birth.

## Operational limits

Run a security lookup before subscribing or trading. The connector stores `exchange|token` in `SecurityId.Native`, because tokens overlap between exchange segments. Flattrade's current scrip-master files do not publish tick size, so that field is left unset during lookup and is populated by the first streaming acknowledgement when available.

Pi documents five market-depth levels. The published limits are 10 order operations per second and 40 per minute, and 40 general API calls per second and 200 per minute. Applications must pace bursts accordingly. GTT/OCO orders, alerts, payout requests, and product conversion are Pi-specific workflows outside the standard StockSharp order lifecycle and are not exposed by this adapter.

## Official references

- [Flattrade Pi API documentation](https://pi.flattrade.in/docs)
- [Flattrade authorization portal](https://auth.flattrade.in/)
- [Official Python examples](https://github.com/flattrade/pythonAPI)
- [Pi API usage terms](https://pi.flattrade.in/terms)
