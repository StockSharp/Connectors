# uSMART OpenAPI Connector for StockSharp

This connector integrates StockSharp with the official uSMART Securities OpenAPI for Hong Kong, U.S., Shanghai, and Shenzhen securities.

## Features

- Signed REST requests using the channel-specific MD5withRSA private key and URL-safe Base64 encoding.
- Real-time WebSocket subscriptions for Level1 quotes, ticks, and order books.
- WebSocket authentication, ping/pong handling, reconnect, and subscription recovery.
- Security lookup for Hong Kong, U.S., Shanghai, and Shenzhen markets.
- REST snapshots for Level1, ticks, and market depth.
- Historical 1, 5, 10, 15, 30, and 60 minute candles plus daily and weekly candles.
- Regular and fractional stock orders, order amendment, and cancellation.
- Regular, pre-market, post-market, and dark-pool U.S. trading sessions where permitted.
- Current-day orders, individual execution records, assets, and positions.

Every request, response, and WebSocket message uses a typed DTO. The connector does not use untyped JSON containers, dynamic objects, or dictionary-shaped protocol payloads.

## Configuration

- `AccessToken` - token obtained through the uSMART OpenAPI login or onboarding flow.
- `ChannelId` - channel identifier assigned by uSMART after approval.
- `PrivateKey` - PEM-encoded channel RSA private key used for request signatures.
- `FundAccount` - account number exposed as the StockSharp portfolio name.
- `EncryptedTradePassword` - optional already-encrypted trading password. It must be produced with the separate data-encryption public key supplied by uSMART; the connector never accepts or stores a plain trading password.
- `IsDemo` - use the documented UAT endpoints.
- `DefaultMarket` - fallback market (`hk`, `us`, `sh`, or `sz`) when a security has no board code.

OpenAPI access is not automatically enabled for every application account. uSMART requires an application, assets in the brokerage account, an authorization agreement, IP whitelisting, and channel-specific key material.

The documented UAT trading host uses plain HTTP, while production uses HTTPS. Do not send a production token to the UAT environment. Quote REST and both WebSocket environments use TLS.

## Behavior and limitations

The public WebSocket contract covers quotes only. Orders, executions, assets, and positions are refreshed through signed REST requests. The connector uses the documented individual transaction-record endpoint for executions instead of fabricating fills from cumulative order quantities.

The OpenAPI login procedure requires a second RSA public key for encrypting phone, login password, and trading password fields. That key is agreed separately with uSMART and is not published. The connector therefore starts from an issued access token and accepts only an already-encrypted optional trading password.

Candlestick history is subject to the account quota documented by uSMART. WebSocket subscription operations are limited to ten topics per request and ten topics per second. Market-data availability depends on the account's quote entitlements.

## Documentation

- [StockSharp uSMART connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/usmart.html)
- [Official uSMART OpenAPI introduction](https://api-doc.usmart.sg/)
- [Basic quotes REST API](https://api-doc.usmart.sg/quote-base.html)
- [Quote WebSocket protocol](https://api-doc.usmart.sg/quote-push.html)
- [Trading and account API](https://api-doc.usmart.sg/trade.html)
- [OpenAPI application page](https://www.usmart.sg/open-api)
