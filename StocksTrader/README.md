# StockSharp StocksTrader Connector

The connector integrates StockSharp with the official StocksTrader REST API.

Supported features:

- demo and real account discovery and account-state polling;
- instrument lookup and latest bid, ask, and last-price snapshots;
- market, limit, and stop order registration;
- pending-order modification and cancellation;
- open-position stop-loss and take-profit modification;
- position closing, order and deal history, and execution reconciliation.

StocksTrader does not expose a streaming market-data API. Level 1 requests
therefore return the latest available snapshot and complete immediately.

Configure a bearer token generated in the StocksTrader web terminal. The
account ID is optional when exactly one account matches the selected demo or
real mode.

Official API documentation: https://api-doc.stockstrader.com/
