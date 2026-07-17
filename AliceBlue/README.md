# Alice Blue ANT API connector

This directory contains the Alice Blue ANT API v2 connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Protocol coverage

- Current ANT v2 JSON REST endpoints at https://a3.aliceblueonline.com for profile, orders, trades, funds, positions, and holdings.
- Enhanced v2 contract masters for NSE, BSE, NFO, BFO, CDS, BCD, MCX, and indices.
- The official Noren market WebSocket for touchline and five-level depth data.
- The separate official order-status WebSocket for realtime order and fill updates.
- Official one-minute and daily historical candles for the exchange segments supported by the Alice Blue history service.

All REST bodies, REST responses, WebSocket commands, and WebSocket events use typed DTOs. The connector does not construct protocol messages with JObject, JArray, JToken, dynamic objects, anonymous objects, or protocol dictionaries.

## Authentication

Configure the Alice Blue user ID and the current userSession bearer token returned by the official authorization flow. ClientId defaults to the client ID returned by the profile endpoint and then to UserId when left empty. The connector does not store the authorization code, application secret, account password, or TOTP seed.

The market WebSocket requires a separately created socket session and a double SHA-256 digest of userSession. The connector creates that session through the official REST endpoint. The order WebSocket uses an order token obtained through its own official REST endpoint.

## Security identifiers and history

Run a security lookup before subscribing or trading. The connector stores exchange|instrumentId in SecurityId.Native because instrument IDs overlap between exchange segments.

Alice Blue currently documents historical data only for one-minute and daily resolution. On weekdays the history endpoint is available outside market hours, and BSE, BFO, and BCD history is not currently documented as supported. These server restrictions are surfaced rather than hidden.

## Official references

- [Alice Blue ANT API v2 documentation](https://v2api.aliceblueonline.com/)
- [Authentication](https://v2api.aliceblueonline.com/Authentication/)
- [Orders](https://v2api.aliceblueonline.com/orders%20Management/)
- [Market WebSocket](https://v2api.aliceblueonline.com/Websocket/)
- [Order-status WebSocket](https://v2api.aliceblueonline.com/Webhooks/)
- [Contract masters](https://v2api.aliceblueonline.com/Contract%20Master/)
- [Historical data](https://v2api.aliceblueonline.com/Historical%20Data/)
