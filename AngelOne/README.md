# Angel One SmartAPI connector

This directory contains the Angel One SmartAPI connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Protocol coverage

- SmartAPI REST endpoints for daily TOTP login, logout, profile, funds, positions, holdings, order book, trade book, order placement, modification, cancellation, and historical candles.
- SmartAPI WebSocket Streaming 2.0 at `wss://smartapisocket.angelone.in/smart-stream`, including the documented binary LTP, quote, and Snap Quote packet layouts.
- Five-level market depth from Snap Quote packets. Angel One discontinued the separate 20-level beta feed in April 2025.
- The dedicated order-status WebSocket at `wss://tns.angelone.in/smart-order-update` for realtime order and fill updates.
- The official instrument master for NSE, NFO, BSE, BFO, MCX, NCDEX/NCO, and CDS instruments.
- Historical candles at 1, 3, 5, 10, 15, and 30 minutes, one hour, and one day. Long ranges are split according to the documented maximum days per request.

All REST payloads, WebSocket commands, and responses use typed DTOs. Market-data packets are decoded directly from the documented little-endian binary layout.

## Authentication

Create a SmartAPI key in the Angel One developer portal and configure:

- `Login`: the Angel One client code.
- `Password`: the trading PIN.
- `ApiKey`: the SmartAPI key.
- `TotpSecret`: the Base32 secret shown below the Angel One QR code. The connector generates the current six-digit TOTP locally.
- `ClientPublicIp`: the static public IP registered for the API key.
- `ClientLocalIp` and `MacAddress`: the values sent in the mandatory SmartAPI headers.

Angel One validates the registered static IP for order operations. API access, TOTP enrollment, and retail-algo registration remain subject to Angel One and exchange rules.

## Security identifiers

Run a security lookup before subscribing or trading. The connector stores `exchangeType|token` in `SecurityId.Native`; this avoids ambiguity because numeric tokens can overlap across exchange segments. For order routing, the original board code from the instrument master is retained.

## Official references

- [SmartAPI documentation](https://smartapi.angelone.in/docs/Instruments)
- [SmartAPI WebSocket Streaming 2.0](https://smartapi.angelone.in/docs/WebSocket2)
- [SmartAPI FAQ](https://smartapi.angelone.in/faq)
- [Exchange regulations](https://smartapi.angelone.in/exchange-regulations)
- [Official Python SDK](https://github.com/angel-one/smartapi-python)
