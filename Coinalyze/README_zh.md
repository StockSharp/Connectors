# Coinalyze 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Coinalyze 连接器**将 StockSharp 接入 Coinalyze 加密货币市场分析 API。它把历史价格和衍生品指标映射为期货或现货品种的标准 StockSharp 周期 K 线。

## 主要功能

- 选择期货或现货品种，并可按交易所限制品种发现。
- 下载价格、持仓量、资金费率、强平或多空比的历史 K 线。
- 使用 Coinalyze API 支持的时间周期。
- 可选择将持仓量和强平数值转换为美元。
- 将单次请求的历史记录上限配置为最多 2,000 条。
- 使用 Coinalyze API 令牌验证请求。

## 适用场景

该连接器适合回测、衍生品研究和 Coinalyze 历史指标的比较分析。订阅前请选择市场类型和 K 线指标；需要缩小研究范围时可设置交易所筛选条件。

此适配器只通过 REST 提供历史数据，不支持实时 K 线更新、Level 1 行情、逐笔成交、市场深度、投资组合或订单执行。可用代码、周期、历史深度和请求频率由 Coinalyze API 决定。
