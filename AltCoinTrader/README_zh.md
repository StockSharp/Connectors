# AltCoinTrader 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**AltCoinTrader 连接器**将 StockSharp 接入南非 AltCoinTrader 现货市场。其以 ZAR 计价的订单簿适合本地价格发现、市场监控、数据采集和加密货币自动化交易。

## 主要功能

- 获取现货品种、交易状态、价格与数量精度以及最小订单金额。
- Level 1 行情、Level 2 订单簿和公开成交。
- 通过公共 WebSocket 实时订阅行情、深度和成交。
- 通过 REST 获取市场快照和近期公开成交。
- 通过认证 WebSocket 获取余额、活动与历史订单、私有成交和账户实时更新。
- 支持 GTC、IOC、FOK 限价单、市价单、单笔撤单和按条件批量撤单。
- 可配置 REST 与 WebSocket 服务地址。

公开市场数据无需凭证。投资组合和交易功能需要具备相应权限的 AltCoinTrader API 密钥与 Secret。
