# Motilal Oswal MO API connector

This directory contains the Motilal Oswal MO API connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Protocol coverage

- MO API REST endpoints for the instrument and index masters, order placement, modification and cancellation, order and trade books, positions, depository holdings, margin details, the client profile, and the account broadcast limit.
- The official binary market WebSocket at `wss://ws1feed.motilaloswal.com/jwebsocket/jwebsocket` for realtime Level1, last-trade, open-interest, circuit-limit, index, and five-level order-book updates.
- The official JSON order WebSocket for realtime account-wide order and trade updates. Live and UAT endpoints are selected with `IsDemo`.
- Portfolio snapshots from margin, position, and holding endpoints, refreshed every 30 seconds while a live portfolio subscription is active.

Binary market messages are decoded directly from the official 30-byte packet layout.

MO API does not document historical ticks, historical order books, or a historical candle query. Its EOD endpoint provides the current exchange snapshot rather than a requested historical interval, so the connector intentionally does not advertise historical market data.

## Authentication

Configure the API key, API secret, client code, current daily authentication token, and access token issued by the MO API login flow. The connector accepts these already-issued tokens and does not store the account password or TOTP secret.

MO API requires local and public IPv4 addresses, a MAC address, vendor information, and an installed-application identifier on every REST request. Configure real values registered for the application; the defaults are placeholders suitable only for initial setup.

`IsDemo` selects the UAT REST and order-stream endpoints. The official SDK uses the same market-broadcast endpoint for live and UAT sessions, and market data remains subject to the account's exchange entitlements.

## Security identifiers

Run a security lookup before subscribing or trading. The connector stores `exchange|scripCode` in `SecurityId.Native`, preserving the exchange segment when numeric scrip codes overlap.

Supported mappings cover NSE and BSE cash, NSE and BSE derivatives and currency, MCX, and NCDEX. The binary feed exposes five order-book levels. The connector reads and enforces the account-specific broadcast subscription limit returned by MO API.

## Official references

- [MO API documentation](https://invest.motilaloswal.com/moAPI/APIDocumentation/Introduction)
- [Official .NET SDK](https://github.com/motradingapi/DotNetSDK)
- [Official Python SDK](https://github.com/motradingapi/PythonSDK)
- [Motilal Oswal developer login](https://invest.motilaloswal.com/moAPI/)
