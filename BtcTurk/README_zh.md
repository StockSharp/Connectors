# BtcTurk 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**BtcTurk 连接器**将 StockSharp 连接到土耳其加密货币现货交易所 BtcTurk Kripto。它适用于通过 StockSharp 标准消息模型处理 TRY、BTC、USDT 及其他市场的交易和行情系统。

## 主要功能

- 获取现货交易品种及其价格、数量和订单限制。
- Level 1 行情、Level 2 订单簿快照和公开成交。
- 通过 WebSocket 接收实时行情、订单簿和成交。
- 获取 BtcTurk 所支持周期的历史 OHLCV K 线。
- 查询投资组合余额、当前及历史订单和账户成交。
- 提交市价、限价、止损市价和止损限价订单。
- 撤销单个订单或一组订单。
- 可配置 REST、历史数据和 WebSocket 服务地址。

## 典型用途

该连接器可用于面向 BtcTurk Kripto 现货市场的交易机器人、终端、数据采集器、订单管理和监控系统。

公开行情无需凭据。交易和账户操作需要具备相应权限的 BtcTurk API 密钥及 Base64 编码的密钥。对于市价买单，BtcTurk 将数量解释为计价货币金额；其他订单的数量以基础资产表示。
