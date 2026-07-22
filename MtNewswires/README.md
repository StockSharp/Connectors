# StockSharp MT Newswires connector

The connector delivers licensed MT Newswires articles as StockSharp news messages.
It uses the publicly documented MT Newswires Global dataset distributed by
[viaNexus](https://vianexus.com/), a product of Blue-Sky Nexus. Direct MT Newswires
API, FTP, and RSS delivery contracts are customer-specific and are not published as
a common wire schema.

## Supported features

- live global or symbol-filtered news through latest-record REST polling;
- historical news by UTC date range;
- headline and full article body when included in the subscription;
- primary ticker and ISIN mapping to `SecurityId`;
- configurable viaNexus data source and dataset identifiers;
- bounded retries for rate limits and transient server failures;

The MT Newswires Global dataset documentation marks its Streaming API as unavailable,
so the adapter polls the documented REST endpoint and uses an overlap window plus
article-ID deduplication. It does not emulate or invent a WebSocket protocol.

## Configuration

1. Obtain a subscription and API token from viaNexus for the MT Newswires dataset.
2. Set `Token` on `MtNewswiresMessageAdapter`.
3. Keep `DataSource` as `EDGE` and `DatasetId` as `MT_NEWSWIRES_Global`, unless your
   entitlement specifies different identifiers.
4. Subscribe to `DataType.News`. Set `SecurityId.SecurityCode` for ticker-filtered
   news, or leave it empty for the global feed.

The API token is sent using the documented `token` query parameter. Authenticated
request URIs are never written to connector errors or logs.

## Documentation

- [MT Newswires official site](https://www.mtnewswires.com/)
- [MT Newswires research and market data](https://www.mtnewswires.com/research-and-market-data)
- [viaNexus MT Newswires Global dataset](https://console.blueskyapi.com/docs/EDGE/news/MT_NEWSWIRES_Global)
- [viaNexus API base URL](https://console.blueskyapi.com/docs/api-basics/base-url)
- [viaNexus query parameters](https://console.blueskyapi.com/docs/api-basics/query-parameters)

Data provided by [viaNexus](https://vianexus.com/), a product of Blue-Sky Nexus.
