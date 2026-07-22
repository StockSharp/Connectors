# ICICI Direct Breeze API connector

The connector integrates StockSharp with the current ICICI Direct Breeze API for NSE cash and NSE equity derivatives.

## Features

- REST v1 authentication using the Breeze API key, secret key, daily API session, timestamp, and SHA-256 checksum;
- official NSE and NFO security master with equities, futures, and options;
- live Level 1, trades, and five-level order books over the Breeze Socket.IO market feed;
- historical candles for 1 second, 1 minute, 5 minutes, 30 minutes, and 1 day;
- live 1-second, 1-minute, 5-minute, and 30-minute OHLC updates over the dedicated OHLC Socket.IO feed;
- order placement, modification, cancellation, status, and trades;
- real-time order notifications;
- funds, positions, and holdings.

## Connection

Create an application in the Breeze portal and configure `Key`, `Secret`, and the daily `ApiSession`. The API session expires every trading day and must be regenerated through the ICICI Direct login flow. Order operations also require the static public IP registered with ICICI Direct.

Market orders are intentionally rejected: the current Breeze trading rules require an explicit limit price. Margin and Option Plus order flows are outside the connector's regular order lifecycle.

The connector enforces the published 2,000-instrument combined live/OHLC subscription limit. ICICI Direct additionally applies 100 REST requests per minute, 5,000 REST requests per day, and a combined order-operation rate limit of 10 requests per second.

Official documentation: https://api.icicidirect.com/breezeapi/documents/index.html

Official SDK: https://github.com/Idirect-Tech/Breeze-Python-SDK
