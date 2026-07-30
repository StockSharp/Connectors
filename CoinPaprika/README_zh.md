# CoinPaprika 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**CoinPaprika 连接器**将 StockSharp 接入 CoinPaprika 加密货币市场数据 API。它提供全球币种资料或所选交易所的市场列表，以及行情快照和 OHLCV 历史 K 线。

## 主要功能

- 在 CoinPaprika 中发现全球币种，或将品种限定到已配置的交易所。
- 选择行情和 K 线请求所使用的计价货币。
- 获取包含价格、24 小时成交量、涨跌和可用市场状态的 Level 1 快照。
- 按可配置的间隔通过 REST 轮询刷新 Level 1 数据。
- 下载指定周期的 OHLCV 历史 K 线。
- 无令牌使用免费 API，或配置令牌访问专业端点及更多权限。
- 将历史响应限制为最多 366 条记录。

## 适用场景

该连接器适合获取加密货币基础资料、进行轻量价格监控和 OHLCV 历史研究。请求数据前请选择全球或交易所范围，并设置计价货币。

CoinPaprika 是数据聚合商，并非交易场所。适配器不提供订单、投资组合、逐笔成交或市场深度。它也不支持历史 Level 1 事件和实时 K 线更新。日内历史、覆盖范围、响应大小和速率限制取决于 CoinPaprika API 套餐及令牌。
