# NovaDAX 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**NovaDAX 连接器**将 StockSharp 接入 NovaDAX 加密货币现货市场。该交易所专注于巴西雷亚尔交易对，因此适合监控巴西加密市场、采集数据和进行自动化交易。

## 主要功能

- 获取现货品种、交易状态、价格与数量精度以及最小下单限制。
- Level 1 行情、Level 2 订单簿、公开成交和 OHLCV K 线历史。
- 通过 Socket.IO 实时订阅行情、深度和成交。
- 通过 REST 获取市场快照、近期成交和历史 K 线。
- 查询余额、活动与历史订单、订单状态和私有成交。
- 支持市价、限价、止损市价和止损限价订单，可按单笔或品种撤单。
- 可配置 REST 与 Socket.IO 地址、子账户标识和 Engine.IO 协议版本。

公开市场数据无需凭证。投资组合和交易功能需要 NovaDAX API 密钥与 Secret；如有需要，还可以指定子账户标识。
