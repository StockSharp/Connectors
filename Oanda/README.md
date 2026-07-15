# OANDA Connector

This directory contains the OANDA v20 connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

The connector supports practice and live accounts, account and portfolio information, order management, positions, executions, instrument lookup, historical candles, streaming prices, and streaming transaction updates.

## Configuration

- `Token` - personal access token generated for the OANDA v20 account.
- `IsDemo` - selects the practice environment when enabled and the live environment when disabled.
- `UseCompression` - enables HTTP response compression.
- `LogOnlyTransactions` - suppresses raw pricing stream log messages while retaining transaction stream logging.

Available instruments, prices, order types, and trading permissions depend on the OANDA division and the selected account.

## Documentation

- [StockSharp OANDA connector documentation](https://doc.stocksharp.com/en/topics/api/connectors/forex/oanda.html)
- [Official OANDA v20 API documentation](https://developer.oanda.com/rest-live-v20/introduction/)
- [Official OANDA development guide](https://developer.oanda.com/rest-live-v20/development-guide/)
