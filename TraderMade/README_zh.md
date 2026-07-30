# TraderMade 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**TraderMade 连接器**将 StockSharp 接入 TraderMade 的外汇和加密货币行情服务。它把 REST 历史数据和 WebSocket 报价映射为统一的 StockSharp 市场数据模型。

## 主要功能

- 根据提供商货币列表与配置的计价货币生成交易对，或使用明确的代码列表。
- 通过流式 API 获取实时 Level 1 买价、卖价和中间价。
- 账户具备权限并启用后，可获取 TraderLadder 订单簿数据。
- 通过 REST 获取历史周期 K 线，并可选择包含周末加密货币数据。
- 独立的 REST 与流式密钥支持仅历史、仅实时或组合配置。
- K 线订阅是有限的历史请求；不支持实时 K 线更新或逐笔成交订阅。
- 本连接器仅提供市场数据，不提供投资组合、余额或下单功能。
- TraderMade 代码、传输和响应格式均封装在标准 StockSharp API 之后。

## 适用场景

适用于不需要经纪执行的外汇和加密货币看板、实时报价监控、图表、分析及历史回测。

可用交易对、TraderLadder 深度、K 线周期与历史、请求限制、周末数据和流式权限由 TraderMade 及 API 套餐决定。
