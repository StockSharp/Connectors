# Variational Omni connector for StockSharp

This connector uses Variational Omni's public read-only REST API. It discovers
the currently listed perpetual markets and polls their latest market statistics
without credentials.

Supported functionality:

- perpetual-market discovery;
- current mark price exposed as the StockSharp theoretical price;
- current bid and ask from the API-provided base quote when available, with the
  documented $1,000, $100,000, and $1,000,000 quotes used as fallbacks;
- 24-hour USDC turnover and aggregate long-plus-short open interest;
- configurable polling with the documented per-IP rate limit enforced by the
  transport.

Variational does not currently publish an Omni trading API or public WebSocket.
The connector therefore does not advertise orders, portfolios, trades, candles,
or market depth. The notional quote curve is not converted into a synthetic
order book. Variational notes that quote values may be cached for up to ten
minutes; every update retains the source quote timestamp.

Official resources:

- [Variational Omni API documentation](https://docs.variational.io/technical-documentation/api)
- [Variational Omni](https://omni.variational.io/)
- [Variational media kit](https://docs.variational.io/more/media-kit)
- [StockSharp Variational Omni connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/variational_omni.html)

Variational Omni and its marks are trademarks of their respective owner.
StockSharp is not affiliated with or endorsed by Variational.
