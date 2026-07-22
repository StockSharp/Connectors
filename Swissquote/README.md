# StockSharp Swissquote connector

StockSharp adapter for Swissquote's professional OpenWealth Trading and Custody REST APIs.

## Credentials and setup

The APIs are offered to Swissquote professional clients. Obtain an OAuth bearer token
from Swissquote and configure `Token`. For securities orders, configure the default
`Safekeeping account`, `Cash account`, and account currency. `Customer ID` is optional
and narrows account discovery. The trading simulator is selected with `IsDemo`; the
published Custody API currently has a production endpoint only.

## Supported operations

- create, cancel, and query orders through OpenWealth Trading;
- market, limit, stop, and stop-limit instructions;
- day/GTC/GTD/IOC/FOK duration mapping supported by the published schema;
- securities, futures, options, funds, bonds, digital assets, and spot-FX identifiers;
- customer/account discovery, current positions, market/cost prices, and buying power;
- account transaction retrieval and trade messages;
- account-scoped security discovery from custody positions.

The connector intentionally does not advertise order replacement because the published
REST contract exposes create, cancel, and status operations only. A replacement must be
performed as cancel plus a new order, or through a separately contracted FIX workflow.

## Market-data limitation

Swissquote's institutional comparison currently lists no securities market-data service
for this REST API. FIX market data is listed for digital assets and requires separate FIX
onboarding. This connector therefore does not fabricate a WebSocket feed or advertise
Level1/depth/tick subscriptions. Position valuation prices come from the Custody API and
are emitted as position fields, not as exchange quotes.

The Custody transaction endpoint is end-of-day in the current public specification, so
REST transaction polling is not a substitute for a real-time FIX execution session.

## Official documentation

- [Swissquote securities and crypto APIs](https://www.swissquote.com/en/institutional/solutions/technology/apis/securities-cryptos)
- [OpenWealth Trading API documentation](https://bankingapi.swissquote.ch/resources/protrading-external-trading-service-api-documentation/ow-professional-trading-api/)
- [OpenWealth Trading OpenAPI contract](https://bankingapi.swissquote.ch/resources/protrading-external-trading-service-api-documentation/openapi/swissquoteOWTradingServicesAPI.yaml)
- [OpenWealth Custody OpenAPI contract](https://bankingapi.swissquote.ch/resources/protrading-external-trading-service-api-documentation/openapi/swissquoteOWCustodyServicesAPI.yaml)
