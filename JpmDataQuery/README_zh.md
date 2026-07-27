# J.P. Morgan DataQuery 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**J.P. Morgan DataQuery 连接器**将 StockSharp 接入专业市场数据与分析服务。 它把提供商特有的数据和操作转换为统一的 StockSharp 消息模型，使应用程序能够在不同场所使用相同的订阅和工作流程。

## 主要功能

- 典型覆盖范围：股票、期货、期权、外汇和差价合约、大宗商品。
- 发现交易品种并获取提供商参考数据。
- 适配器支持的市场数据：Level 1 行情。
- 请求历史数据，用于图表、分析和策略回测。
- 此适配器用于获取市场数据，不提供订单路由。
- 提供商特有的传输、会话和数据格式均封装在标准 StockSharp API 之后。

## 适用场景

适用于使用提供商数据驱动图表、市场数据存储、分析、研究和策略测试。

可用交易品种、数据深度、交易权限、请求限制和服务可用性由 J.P. Morgan DataQuery、API 套餐及所连接账户的权限决定。
