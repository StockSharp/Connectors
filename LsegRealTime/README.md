# StockSharp LSEG Real-Time Connector

The connector integrates StockSharp with the official LSEG Real-Time WebSocket API (`tr_json2`). It supports both a deployed RTDS endpoint and LSEG Real-Time Optimized cloud access.

## Supported functionality

- Deployed RTDS login with a DACS user name.
- LSEG Delivery Platform OAuth v1 password grant and OAuth v2 client credentials.
- Cloud WebSocket endpoint discovery, access-token renewal and Login reissue.
- Optional two-endpoint hot standby with subscription recovery and failover.
- Source Directory service-state processing.
- Exact RIC lookup through a non-streaming `MarketPrice` snapshot.
- Streaming Level 1 and ticks through `MarketPrice`.
- Streaming incremental and multipart snapshot order books through `MarketByPrice`.

## Configuration

For a deployed RTDS environment, select `Deployed`, enter the DACS login and either provide the WebSocket address or leave it empty to use `ws://localhost:15000/WebSocket`.

For cloud access, select the OAuth flow assigned to the LSEG application. Supply the client ID and either the user credentials for OAuth v1 or the client secret for OAuth v2. When `Address` is empty, the connector resolves an endpoint from the configured discovery URL and region.

`Service` defaults to `ELEKTRON_DD`, `ApplicationId` to `256`, and `Scope` to `trapi.streaming.pricing.read`. These values must match the customer's LSEG environment and entitlements. Hot standby requires two explicit addresses or two suitable single-location endpoints returned by service discovery.

## Limitations

- LSEG account access, DACS permissions and instrument entitlements are not included with the connector.
- Security lookup is an exact RIC snapshot, not a broad text search or a complete instrument catalogue.
- The connector does not expose history, news, analytics domains or trading/order routing.
- Field availability depends on the selected service, instrument and entitlements. Instrument type inference is used when `RECORDTYPE` does not identify the asset class.
- The `tr_json2` gateway resolves field identifiers to names, so the connector does not download or redistribute raw LSEG data dictionaries.
- Use of and redistribution of LSEG data remain subject to the customer's LSEG agreements.

## Official documentation

- [LSEG WebSocket API](https://developers.lseg.com/en/api-catalog/real-time-opnsrc/websocket-api)
- [LSEG Real-Time platform](https://developers.lseg.com/en/use-cases-catalog/real-time)
- [Official WebSocket API examples](https://github.com/Refinitiv/websocket-api)
- [WebSocket API protocol specification](https://developers.lseg.com/content/dam/devportal/realtimeapi_pdfs/websocket_api_protocol_specification.pdf)
