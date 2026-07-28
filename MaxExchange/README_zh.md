# MAX Exchange 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**MAX Exchange 连接器**将 StockSharp 连接到 MaiCoin Group 运营的台湾现货交易所，特别适合 TWD 和 USDT 加密货币市场。

## 主要功能

- 获取现货交易品种及其交易状态、精度和最小订单信息。
- Level 1 行情、Level 2 订单簿、公开成交和 OHLCV K 线。
- 通过 WebSocket 实时接收行情、订单簿、成交和 K 线。
- 通过 REST API v3 获取历史 K 线和最新市场快照。
- 查询余额、未成交与历史订单以及私有成交。
- 支持市价、限价、止损市价、止损限价、Post-only 和 IOC 限价订单。
- 支持单笔与批量撤单，并可配置 REST 和 WebSocket 地址。

## 典型用途

可用于交易机器人、交易终端、TWD 市场数据采集、监控服务和订单管理系统。

公开市场数据无需凭证。账户查询和交易操作需要 MAX Exchange API 密钥和密钥 Secret。
