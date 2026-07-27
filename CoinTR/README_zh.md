# CoinTR 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**CoinTR 连接器**将 StockSharp 连接到面向土耳其市场的加密货币交易所 CoinTR，并通过 StockSharp 标准消息模型提供 CoinTR 现货交易品种。

## 主要功能

- 获取现货交易品种、价格和数量精度以及交易限制。
- Level 1 行情、Level 2 订单簿快照和公开成交。
- 通过 WebSocket 接收实时行情、订单簿、成交和 K 线。
- 获取 CoinTR 支持周期的历史 OHLCV K 线。
- 获取投资组合余额、活动订单和私有成交通知。
- 提交市价、限价和触发订单并撤销订单。
- 可配置 REST、公共 WebSocket 和私有 WebSocket 地址。

## 典型用途

该连接器适用于在 CoinTR 现货市场运行的交易机器人、终端、市场数据采集器、监控工具和订单管理服务。

公共市场数据无需凭据。交易和账户操作需要具有相应权限的 API 密钥、密钥口令和密码短语。对于市价买单，CoinTR 将数量解释为计价货币金额；限价单和市价卖单使用基础资产数量。
