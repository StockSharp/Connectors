# QMT Connector for StockSharp

This connector integrates StockSharp with the official XtQuant API supplied with
MiniQMT. XtQuant is a local Python API, so the package includes a small, TCP
gateway in `Gateway/qmt_gateway.py`. The adapter does not embed Python, start
MiniQMT, or manage the broker installation.

## Prerequisites

- A broker-provided MiniQMT installation and an account with XtQuant permission.
- The XtQuant package distributed for that exact MiniQMT version.
- Python 3.9 or later supported by that XtQuant distribution.
- MiniQMT started and logged in before the gateway.

Broker availability, supported MiniQMT versions, market-data entitlements, and
redistribution rights vary. Confirm them with the broker before deployment.

## Gateway

Start the gateway separately from the MiniQMT `userdata_mini` directory:

```powershell
python .\Gateway\qmt_gateway.py `
  --qmt-path "C:\QMT\userdata_mini" `
  --account "YOUR_ACCOUNT_ID" `
  --token "A_LONG_RANDOM_SECRET"
```

The default endpoint is `127.0.0.1:58630`. Use a unique `--session-id` when more
than one XtQuantTrader process is running. `--quote-port` can select an explicit
MiniQMT quote port. The `--sectors` option controls the XtQuant sectors used by
security lookup.

The gateway uses a versioned, four-byte length-prefixed UTF-8 JSON protocol. It authenticates every connection with the shared secret, forwards official XtQuant callbacks, shares identical native market subscriptions, and restores them after a MiniQMT reconnect.

## Configuration

- `GatewayHost` — host running the gateway; `127.0.0.1` by default.
- `GatewayPort` — gateway TCP port; `58630` by default.
- `GatewayToken` — the same shared secret passed with `--token`.
- `ReconnectAttempts` — adapter reconnect attempts after a gateway disconnect.
- `RequestTimeout` — request timeout in seconds.

## Supported data and trading

- Shanghai, Shenzhen, and Beijing instruments available to MiniQMT.
- Security lookup and instrument details.
- Level1 and order-book snapshots from the official `tick` callback.
- Time-frame candle history and real-time candles.
- Level-2 transaction ticks when the account has the required entitlement.
- Account assets, positions, orders, and executions.
- Stock limit and market order registration and cancellation.

Historical candles must already be available in MiniQMT local data. The gateway
does not initiate bulk history downloads. XtQuant does not document live asset or
position callbacks, so StockSharp refreshes those values periodically while a
portfolio subscription is active. Server-side conditional orders, time-in-force
selection, order replacement, and group cancellation are not exposed.

## Documentation

- [StockSharp QMT connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/qmt.html)
- [Official XtQuant getting started](https://dict.thinktrader.net/nativeApi/start_now.html)
- [Official XtData reference](https://dict.thinktrader.net/nativeApi/xtdata.html)
- [Official XtQuantTrader reference](https://dict.thinktrader.net/nativeApi/xttrader.html)
- [Official XtQuant downloads](https://dict.thinktrader.net/nativeApi/download_xtquant.html)
