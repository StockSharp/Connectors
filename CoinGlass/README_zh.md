# CoinGlass 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**CoinGlass 连接器**将 StockSharp 接入 CoinGlass 加密货币市场分析 API。它把所选的期货、现货、期权、Bitcoin ETF 和 Ethereum ETF 数据集映射为 StockSharp 品种、Level 1 消息和历史 K 线。

## 主要功能

- 选择 CoinGlass 市场类型，并可按交易所或代码限制请求。
- 发现已配置数据集中可用的品种。
- 获取当前 Level 1 指标，包括数据源提供的价格、成交量、涨跌和持仓量。
- 按可配置的间隔轮询 Level 1 快照。
- 下载价格、持仓量、资金费率或强平指标的指定周期历史序列。
- 将单次请求的历史记录上限配置为最多 1,000 条。

## 适用场景

该连接器适合构建研究面板、监控衍生品以及分析 CoinGlass 指标历史。请配置 API 令牌，选择市场类型和指标；需要聚焦数据时可限定交易所或代码。

CoinGlass 是分析数据源，而不是交易执行场所。适配器不提供订单、投资组合、逐笔成交或市场深度。它不支持历史 Level 1 事件和实时 K 线更新，K 线请求只返回历史数据。数据集可用性和请求限额取决于 CoinGlass 订阅方案。
