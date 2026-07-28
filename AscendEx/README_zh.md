# AscendEX 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**AscendEX 连接器**将 StockSharp 接入已发布的 AscendEX Pro API。单个适配器覆盖现货 cash、杠杆和永续合约市场，适合多市场加密策略，也可保留该平台公开协议的完整实现。

## 主要功能

- 获取现货、杠杆和永续合约品种，包括交易状态、价格与数量步长以及订单限制。
- Level 1 行情、Level 2 订单簿、公开成交和 OHLCV K 线。
- 通过 REST 获取快照与历史数据，并通过独立的现货和合约 WebSocket 接收实时数据。
- Cash 与 margin 余额、合约保证金与持仓、当前及历史订单和成交。
- 支持 GTC、IOC、FOK、post-only 和合约 reduce-only 的 market、limit、stop-market 与 stop-limit 订单。
- 单笔和批量撤单。
- 可配置 REST、现货 WebSocket、合约 WebSocket、账户组以及 cash/margin 模式。

公开市场数据无需凭据。投资组合和交易功能需要 API 密钥、密钥密码以及 AscendEX 分配的账户组。
