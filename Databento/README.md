# StockSharp Databento connector

The connector integrates StockSharp directly with Databento's official Raw Live
API and Historical API. It has no dependency on the Databento Python, C++, or
Rust clients: control messages and DBN records are represented by native, typed
C# protocol models.

## Supported data

- streaming and historical tick trades through the `trades` schema;
- streaming and historical Level 1 quotes through `mbp-1`;
- ten-level market-depth snapshots through `mbp-10`;
- market-by-order snapshots and events exposed as StockSharp order-log messages
  through `mbo`;
- 1-second, 1-minute, 1-hour, and 1-day OHLCV candles;
- instrument lookup from the `definition` schema, including instrument type,
  venue, currency, tick and lot sizes, multiplier, expiration, strike, option
  side, and underlying;
- `raw_symbol`, `instrument_id`, `parent`, and `continuous` input symbology;
- live CRAM authentication, heartbeats, slow-reader protection, reconnect, and
  restoration of active subscriptions;
- streaming historical downloads with an optional server-side record limit.

The implementation decodes DBN versions 1 through 3, including each version's
definition, symbol-mapping, statistics, error, and system record layouts. Fixed
prices are converted using Databento's `1e-9` scale, and nanosecond timestamps
are converted to UTC `DateTime` values.

## Connection

Set `Key` to the 32-character Databento API key and choose a `Dataset`, such as
`GLBX.MDP3`, `XNAS.ITCH`, or `EQUS.MINI`. The default dataset is `GLBX.MDP3`.

When `LiveAddress` is empty, the connector derives the official gateway address
from the dataset. For example, `GLBX.MDP3` becomes:

```text
glbx-mdp3.lsg.databento.com:13000
```

`HistoricalAddress` defaults to the official streaming endpoint:

```text
https://hist.databento.com/v0/timeseries.get_range
```

Live connectivity is opened lazily on the first real-time subscription. As a
result, a key with historical access can download history without also requiring
a live entitlement for the selected dataset.

Historical requests use uncompressed DBN so the connector can stream and decode
the response without temporary files. Databento bills and licenses data according
to the account, dataset, venue, date range, and schema; use `From`, `To`, and
`Count` deliberately. If no `From` is supplied, the connector limits non-candle
requests to the preceding 24 hours. A count-only candle request derives a bounded
range from its candle interval.

## Protocol behavior

The live Raw API has no unsubscribe control message. StockSharp unsubscriptions
therefore stop local delivery immediately, while the upstream stream remains in
the current TCP session. On reconnect, only subscriptions that are still active
are restored, using Databento's natural-refresh recovery mode.

MBO subscriptions request the official live snapshot before incremental events.
Symbol mapping records bind Databento `instrument_id` values to the resolved raw
symbols before market records are routed. Parent and continuous subscriptions can
therefore produce messages for their resolved contracts rather than disguising
all records as the input alias.

## Limitations

- Databento is a market-data service. The connector intentionally has no order,
  account, portfolio, or position operations.
- `mbp-10` contains at most ten price levels. Use `mbo` order-log data when a
  venue and entitlement provide full market-by-order depth.
- Level 1 subscriptions use `mbp-1`. Databento's separate `statistics`, `status`,
  and `imbalance` schemas are decoded by the protocol layer but are not requested
  automatically, avoiding unsupported-schema errors on datasets that do not
  publish them.
- Catalog lookup uses the definition stream for the latest completed UTC business day.
  Recently listed intraday instruments may first appear in a later lookup.
- Databento may reject a schema, symbol, or history range that is unavailable for
  the chosen dataset or not included in the account's entitlements.

## Official documentation

- [Live API](https://databento.com/docs/api-reference-live)
- [Raw TCP protocol](https://databento.com/docs/api-reference-live/basics/raw-api)
- [Live authentication request](https://databento.com/docs/api-reference-live/client-control-messages/authentication-request?live=raw)
- [Live subscription request](https://databento.com/docs/api-reference-live/client-control-messages/subscription-request?live=raw)
- [Historical `timeseries.get_range`](https://databento.com/docs/api-reference-historical/timeseries/timeseries-get-range?historical=http)
- [Databento Binary Encoding](https://databento.com/docs/knowledge-base/new-users/dbn-encoding)
- [Schemas and data formats](https://databento.com/docs/schemas-and-data-formats)
- [Symbology](https://databento.com/docs/standards-and-conventions/symbology)
- [Datasets and venues](https://databento.com/docs/venues-and-datasets)
- [Official DBN specification source](https://github.com/databento/dbn)
- [Official Rust client protocol source](https://github.com/databento/databento-rs)
