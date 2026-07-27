# Bit2Me 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Bit2Me 连接器**将 StockSharp 连接到西班牙数字资产服务商的现货交易平台 Bit2Me Pro。它适用于需要通过 StockSharp 标准消息模型直接访问具有 EUR 流动性的加密货币市场的系统。

## 主要功能

- 获取 Bit2Me Pro 现货市场及其价格、数量和最小订单规则。
- 通过 REST 获取 Level 1 行情和 Level 2 订单簿快照。
- 通过 WebSocket 实时接收公开成交和完整订单簿更新。
- 获取 Bit2Me 提供周期的历史成交和 OHLCV K 线。
- 提交市价单、限价单和止损限价单。
- 撤销订单并查询订单与成交。
- 获取投资组合余额以及被活动订单冻结的资金。
- 可配置 REST 和 WebSocket 地址，以适应测试、路由或基础设施调整。

## 典型用途

该连接器可用于处理 Bit2Me Pro 现货品种的交易机器人、终端、数据采集器、订单管理服务和监控工具。

公开市场数据无需凭据。交易和账户操作需要具有相应权限的 Bit2Me API 密钥和 Secret。可用市场、请求限制和账户功能由 Bit2Me 控制。
