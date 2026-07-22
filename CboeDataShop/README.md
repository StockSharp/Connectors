# Cboe DataShop / LiveVol connector for StockSharp

This connector provides native .NET access to the official Cboe LiveVol All Access REST API. It uses the published OAuth 2.0 client-credentials flow and the documented `live` or `delayed` All Access routes directly; it does not scrape DataShop, launch another runtime, or simulate a streaming transport.

## Supported functionality

- OAuth 2.0 client-credentials authentication at `https://id.livevol.com/connect/token`, bearer-token caching, and automatic renewal after expiry or an HTTP 401/403 response.
- Configurable live or delayed All Access data environment.
- Current Cboe reference list of US equity symbols and company names.
- Option-chain lookup by underlying, expiry, strike, and call/put type using official OSI option identifiers.
- Current and historical underlying Level1 snapshots: NBBO, last trade, daily OHLCV, midpoint, and normalized 30-day implied volatility.
- Current and historical option Level1 snapshots: NBBO, midpoint, last trade, daily OHLCV, trade count, open interest, theoretical price, implied volatility, and Greeks.
- Native daily OHLCV candles for entitled equities and options. Option candles retain open interest.
- Historical underlying and option time-and-sales, including exchange/condition metadata.
- Sequence-number pagination up to the API's 10,000-record page limit. Canceled trades and cancellation messages are excluded from StockSharp tick output.
- Official Cboe trading-day calendar for date-range requests.
- Retry handling for rate limits and transient server failures.

## Configuration

- `Login` is the OAuth client ID issued for the Cboe All Access application.
- `Password` is its client secret.
- `Address` defaults to `https://api.livevol.com/v1/`.
- `TokenAddress` defaults to `https://id.livevol.com/connect/token`.
- `DataMode` defaults to `Delayed`. Select `Live` only when the application and account have the required live-data and SIP entitlements.

Use securities returned by lookup whenever possible. Equity symbols use board `CBOE`; option OSI symbols use `CBOEOPT` and retain the underlying equity identifier. An option lookup must identify an underlying (through `UnderlyingSecurityId` or an option-only symbol request), because enumerating the complete US option universe is neither a useful nor a responsible API operation.

## Data model and limitations

The market-at-a-glance endpoints are REST snapshots. Even in `Live` mode they do not create a continuous WebSocket subscription, so each StockSharp Level1 request returns the entitled snapshot/history and finishes. The connector intentionally does not advertise a streaming transport or market depth.

Daily candles use only the API's native `underlying_open/high/low/close` or `option_open/high/low/close` fields; intraday candles are not synthesized from snapshots. Time-and-sales output uses actual trade price and size. The official `cancel_flag` values `1` and `2` represent canceled data and are not emitted as trades.

LiveVol reports underlying `iv30` in percentage points (for example, `22.08`); it is normalized to `0.2208` for StockSharp's `ImpliedVolatility` field. Option `iv` and `mid_iv` are already decimal ratios and are preserved. API timestamps are Eastern time and are converted with the America/New_York daylight-saving rules.

All Access is a paid and entitlement-controlled data product. A free trial may have a daily point allowance. SIP fields can be null without CTA, UTP, or OPRA subscriptions, and the available history, rate limits, point costs, redistribution rights, live/delayed access, and fields depend on the customer's Cboe agreement.

The API exposes market data and analytics, not a brokerage order lifecycle. This connector therefore does not advertise accounts, portfolios, positions, orders, or executions.

## Official documentation

- [Cboe LiveVol All Access technical reference](https://www.livevol.com/apis/technical-reference/)
- [Authentication](https://api.livevol.com/v1/docs/Home/Authentication)
- [Option and underlying quotes](https://api.livevol.com/v1/docs/Help/Api/GET-allaccess-market-option-and-underlying-quotes_root_option_type_date_min_expiry_max_expiry_min_strike_max_strike_symbol)
- [Underlying quotes](https://api.livevol.com/v1/docs/Help/Api/GET-allaccess-market-underlying-quotes_symbols_date_seq_no)
- [Option time-and-sales](https://api.livevol.com/v1/docs/Help/Api/GET-allaccess-time-and-sales-option-trades_symbol_root_expiry_strike_option_type_min_time_max_time_seq_no_exchange_id_condition_id_limit_min_size_max_size_min_price_max_price_date)
- [Underlying time-and-sales](https://api.livevol.com/v1/docs/Help/Api/GET-allaccess-time-and-sales-underlying-trades_symbol_min_time_max_time_seq_no_exchange_id_condition_id_limit_min_size_max_size_min_price_max_price_date)
- [Cboe All Access subscription](https://datashop.cboe.com/cboe-all-access-api)
- [StockSharp Cboe DataShop connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/cboe_datashop.html)

Cboe, Cboe DataShop, and LiveVol are trademarks of Cboe Exchange, Inc. StockSharp is not affiliated with or endorsed by Cboe.
