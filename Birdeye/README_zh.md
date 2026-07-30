# Birdeye 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Birdeye 连接器**将 StockSharp 接入 Birdeye 的链上加密货币数据 API。它可针对所选区块链提供代币发现、当前市场指标和 OHLCV 历史数据，默认使用 Solana。

## 主要功能

- 发现所选链上的代币并加载其基础资料。
- 按代币地址限定查询，并可设置最低流动性筛选条件。
- 获取 Level 1 快照，并通过 REST 轮询接收更新。
- 在配置的历史记录上限内下载指定周期的历史 K 线。
- 通过付费 WebSocket 服务启用 Level 1 和 K 线实时更新。
- 以美元或链的原生货币表示价格。
- 使用 Birdeye 支持的周期；一分钟以下的 K 线仅适用于 Solana。

## 适用场景

该连接器适合在 Birdeye 支持的网络上进行代币筛选、链上价格监控和 OHLCV 历史分析。订阅前需配置区块链、API 令牌、计价方式以及可选的发现筛选条件。

Birdeye 是市场数据提供商，因此连接器不提供订单、投资组合、成交执行或订单簿功能。它不支持历史 Level 1 事件；未启用流式模式时，K 线订阅会在历史数据返回后结束。数据覆盖范围、WebSocket 权限和请求限额取决于 Birdeye API 套餐。
