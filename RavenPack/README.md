# RavenPack connector for StockSharp

This connector integrates the RavenPack Analytics and RavenPack Edge dataset APIs. It follows the official API clients: synchronous history is requested from the dataset JSON endpoint and live analytics are consumed from RavenPack's persistent HTTP JSON-lines feed.

## Supported functionality

- Exact security and entity mapping by ticker, MIC listing, ISIN, CUSIP, SEDOL, or name through `/entity-mapping`.
- Entity-reference enrichment for names, listings, MICs, and security identifiers.
- Historical granular news analytics through `POST /json/{dataset_id}`.
- Real-time granular news analytics through `GET /json/{dataset_id}?keep_alive` on the RavenPack feed host.
- Security-specific subscriptions using RavenPack entity IDs and local validation of every returned record.
- RavenPack Analytics (`rpa`) and RavenPack Edge products with their separate official API and feed hosts.
- Bounded feed deduplication, automatic reconnect with backoff, exact UTC conversion, and StockSharp subscription limits.
- Optional licensed story URL resolution through the RavenPack Document API.

## Authentication and dataset setup

Set `Token` to the RavenPack API key supplied with your commercial subscription. The connector sends it in the official `API_KEY` HTTP header and never puts the key in a URL. Set `DatasetId` to an existing granular RavenPack dataset that your key can access.

`Product` defaults to RavenPack Edge and selects these endpoints:

- REST: `https://api-edge.ravenpack.com/1.0/`
- Feed: `https://feed-edge.ravenpack.com/1.0/json/`

Selecting RavenPack Analytics changes the defaults to `https://api.ravenpack.com/1.0/` and `https://feed.ravenpack.com/1.0/json/`. `Address` and `FeedAddress` remain configurable for a provider-supplied environment.

RavenPack's real-time endpoint supports granular datasets only. The connector validates the selected dataset at connection time. For useful live `NewsMessage` values, configure the dataset to include `timestamp_utc`, the product's document/story ID, `rp_entity_id`, entity information, and `title` (Edge) or `headline` (Analytics). Unknown additional fields are safely ignored.

## Data semantics and boundaries

RavenPack delivers one analytics record per detected entity/event, not a consolidated exchange quote. The connector therefore exposes `DataType.News`; it does not advertise Level1, trades, order books, candles, or order routing. A single document can legitimately produce several StockSharp news messages when RavenPack reports analytics for several entities.

Historical requests override the selected dataset to granular frequency and request the documented fields needed by the selected product. RavenPack limits synchronous granular JSON responses to 10,000 records, so `MaxRecords` cannot exceed 10,000. A history-only request without `From` uses `DefaultHistoryLookback`.

Ticker-filtered history uses a `rp_entity_id` filter. The live feed is shared by all StockSharp subscriptions and records are filtered locally by the mapped RavenPack entity ID. Empty and wildcard security lookups return no records because RavenPack's entity-mapping endpoint is an exact mapping service, not a browsable security master.

`IsResolveDocumentUrls` is off by default because URL resolution requires an additional licensed REST call per analytics record. When enabled, unavailable or unlicensed URLs do not suppress the underlying news analytics message.

## Official documentation

- [RavenPack customer API documentation](https://app.ravenpack.com/api-documentation/) (sign-in required)
- [Official RavenPack Python API client](https://github.com/RavenPack/python-api)
- [RavenPack Edge data delivery](https://www.ravenpack.com/products/edge/delivery)
- [RavenPack News Analytics](https://www.ravenpack.com/products/edge/data/news-analytics)
- [RavenPack support](https://www.ravenpack.com/support/)
- [StockSharp RavenPack connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/ravenpack.html)

RavenPack and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by RavenPack.
