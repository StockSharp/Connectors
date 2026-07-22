# StockSharp Saxo OpenAPI connector

This connector integrates StockSharp with the official Saxo OpenAPI. Reference data, historical market data, portfolio snapshots, and trading use the REST API. Realtime prices, market depth, candles, balances, positions, and order activities use Saxo's binary-framed WebSocket stream with typed JSON payloads.

## Configuration

- `Token` is the OAuth access token issued for the OpenAPI application.
- `RefreshToken`, `Key`, `Secret`, and `RedirectUri` enable automatic access-token renewal. They must belong to the same registered application and OAuth session.
- `AccountKey` selects the default trading account. When empty, the connector uses the first account returned by Saxo.
- `Environment` selects the simulation or live REST, streaming, token, and reauthorization endpoints.

Treat every token and the application secret as credentials. Do not write them to logs or source control. The connector refreshes an expiring access token when refresh credentials are present and reauthorizes the active WebSocket session after renewal.

## Supported operations

- instrument lookup across the asset types exposed by Saxo reference data;
- realtime Level1 prices and market depth through info-price subscriptions;
- historical and realtime time-frame candles through Chart v3;
- market, limit, stop, stop-limit, and trailing-stop order types supported by the instrument;
- order replacement and cancellation;
- open-order recovery, audit order activities, and realtime ENS order activities;
- account balance and net-position snapshots plus realtime portfolio updates.

Supported native order types and durations are obtained from each instrument's `OrderSetting` field group and validated before an order is sent. Actual availability depends on the instrument, account, market session, and client entitlements.

Saxo Chart v3 expresses horizons in minutes. The connector exposes every currently documented horizon from one minute through 360 days, requests at most 1,200 samples per REST page, and returns at most 12,000 samples for one StockSharp history request. Saxo info-price streaming is used for quote and depth updates; the connector does not advertise a tick-by-tick trades feed.

## Streaming and limits

The WebSocket client decodes Saxo's binary message envelope (message identifier, reference identifier, payload format, and payload length) and deserializes only typed DTOs. Protocol payloads are not parsed through `dynamic`, untyped JSON objects, anonymous objects, or dictionaries.

Realtime updates may contain only changed fields. The connector preserves the last subscription snapshot when applying incremental depth, candle, balance, and net-position updates. Active market-data and portfolio subscriptions are restored after transport reconnection. Info-price subscriptions use Saxo's one-second minimum refresh rate. Saxo generally permits one primary realtime streaming session per user and applies endpoint-specific request and subscription rate limits; applications must honor current API responses and account entitlements.

## Official references

- [Saxo OpenAPI security](https://www.developer.saxo/openapi/learn/security)
- [OAuth authorization-code grant](https://www.developer.saxo/openapi/learn/oauth-authorization-code-grant)
- [Streaming overview](https://www.developer.saxo/openapi/learn/streaming)
- [Plain WebSocket streaming](https://www.developer.saxo/openapi/learn/plain-websocket-streaming)
- [Trade v2 orders](https://www.developer.saxo/openapi/referencedocs/trade/v2/orders)
- [Reference-data instruments](https://www.developer.saxo/openapi/referencedocs/ref/v1/instruments)
- [Official Saxo OpenAPI samples](https://github.com/SaxoBank/openapi-samples-js)

Verify current Saxo documentation, rate limits, entitlements, and regulatory requirements before production deployment.
