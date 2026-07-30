# MarketData.app 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**MarketData.app 连接器**将 StockSharp 接入专业市场数据服务。它把提供商特有的数据转换为统一的 StockSharp 消息模型，使应用程序能够在不同数据源中使用相同的请求和工作流程。

## 主要功能

- 典型覆盖范围：股票、ETF、期权、指数和基金。
- 发现交易品种（包括期权链查询）并获取提供商参考数据。
- 适配器支持的市场数据：Level 1 行情快照和 K 线。
- 请求历史 K 线，用于图表、分析和策略回测；该服务不提供期权 K 线。
- 此适配器用于市场数据，不提供订单路由。
- 提供商特有的传输、会话和数据格式均封装在标准 StockSharp API 之后。

## 适用场景

适用于图表、交易品种与期权发现、市场数据存储、分析、研究流程以及基于提供商数据的策略测试。

可用交易品种、历史深度、复权方式、请求限制、数据权限和服务可用性由 MarketData.app 及所连接的 API 套餐决定。
