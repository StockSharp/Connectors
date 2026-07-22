# CoinDCX Connector

The connector integrates CoinDCX spot markets through the current REST API and
the exchange's Socket.IO stream.

Supported features:

- security lookup with market limits, price steps, quantity steps, and state;
- Level1 snapshots, order-book snapshots, public trades, and OHLCV candles;
- realtime trades, depth, and candles through the public Socket.IO channels;
- balances, active orders, order status, and account trade history;
- market and limit order registration, individual and bulk cancellation, and
  price editing for active limit orders;
- authenticated balance, order, and trade notifications through the private
  `coindcx` Socket.IO channel.

CoinDCX identifies a market with both a trading symbol such as `BTCUSDT` and an
exchange-qualified pair such as `B-BTC_USDT`. StockSharp security identifiers
use the trading symbol; the qualified pair is retained internally for public
market-data subscriptions.

The streaming transport implements the current Engine.IO 4 WebSocket handshake, ping/pong frames, namespace connection, and channel restoration after reconnect.

API credentials are optional for public market data and required for portfolio
and transaction operations. Private REST requests sign the exact compact JSON
body with HMAC-SHA256 and send the signature in `X-AUTH-SIGNATURE`.

Official API documentation: [CoinDCX API Reference](https://docs.coindcx.com/).
