# Robinhood connector

The connector uses the official Robinhood Agentic Trading MCP endpoint. It supports US equity symbol search, polling Level 1 quotes, historical candles, accounts, balances, positions, equity order history, order review, placement, and cancellation.

Robinhood exposes Streamable HTTP MCP rather than a market-data WebSocket. The connector therefore polls quotes, positions, and orders at the configured interval. Quote calls are automatically split into the API limit of 20 symbols.

## Authentication

1. Create and authenticate a dedicated Robinhood Agentic account on a desktop device.
2. Authorize `https://agent.robinhood.com/mcp/trading` through an MCP-capable client using Robinhood OAuth.
3. Set the resulting OAuth bearer access token in the connector settings.

The connector can read all accounts authorized by Robinhood, but order placement is restricted by Robinhood to the dedicated Agentic account. An equity order is always reviewed with `review_equity_order` before `place_equity_order` is called.

Robinhood can vary the set of MCP tools by account eligibility and rollout. At connection time, the connector reads `tools/list` and rejects operations whose official tool is unavailable.

Official documentation:

- [Agentic Trading overview](https://robinhood.com/us/en/support/articles/agentic-trading-overview/)
- [Trading with your agent and supported tools](https://robinhood.com/us/en/support/articles/trading-with-your-agent/)
- [Model Context Protocol Streamable HTTP transport](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)
