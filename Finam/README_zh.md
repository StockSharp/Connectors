# Finam Trade API 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Finam Trade API 连接器**将 StockSharp 应用程序连接到 Finam 提供的经纪账户和市场数据。它把证券、报价、订单、成交和投资组合状态转换为统一的 StockSharp 消息模型。

## 主要功能

- 查询 Finam 可用的股票、债券、货币、基金、期货和期权。
- Level 1 报价、订单簿、市场成交和分时蜡烛图。
- 历史蜡烛图请求和实时市场数据订阅。
- 提交市价、限价、止损及止损限价订单，并支持撤单。
- 接收订单状态、自有成交、现金余额和持仓更新。
- 自动将 API 密钥换取短期会话令牌。
- REST 与 WebSocket 地址可配置，便于使用兼容网关和测试环境。

## 典型用途

该连接器适用于需要通过统一 StockSharp 接口访问 Finam 的交易机器人、交易终端、投资组合监控和订单管理服务。

连接时需要 Finam Trade API 密钥。可以明确指定账户；如未指定，连接器会选择令牌可访问的第一个账户。证券使用 Finam 的 `ticker@MIC` 格式标识。可用市场、历史深度、实时数据、交易权限和请求限制取决于所连接的账户及 Finam 服务条件。
