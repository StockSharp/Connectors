# Finage 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Finage 连接器**将 StockSharp 接入 Finage 的外汇市场数据服务。它是面向货币工具的只读适配器，将 REST 参考与历史数据同可选的 WebSocket 报价流结合起来。

## 主要功能

- 从已配置的品种列表或提供商的 REST 品种搜索中发现货币对。
- 通过 REST 获取当前最佳买价和卖价快照。
- 配置独立流式令牌后，通过 WebSocket 接收实时 Level 1 买卖价更新。
- 通过 REST 获取历史 K 线，支持 1、5、10、15、30 分钟，1、2、4、6、8、12 小时，1 日和 1 周周期。
- 可配置请求间隔和最大品种数，以控制 REST API 使用量。
- 不支持历史 Level 1 事件和实时 K 线更新。
- 不支持逐笔成交、订单簿、下单、投资组合数据或账户操作。

## 适用场景

该连接器适用于外汇自选列表、报价监控、图表、研究，以及基于 Finage K 线历史的策略回测。

使用时需要 Finage REST API 密钥，实时报价还需要独立的流式令牌。品种覆盖、历史深度、实时权限和请求限制取决于所订阅的 Finage 套餐。
