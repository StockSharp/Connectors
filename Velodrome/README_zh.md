# Velodrome 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Velodrome 连接器**将 StockSharp 接入 Optimism 上的 Velodrome 经典池和 Slipstream 池。它把配置的池、可执行报价、链上兑换、钱包余额和已提交交易映射为 StockSharp 消息。

## 主要功能

- 发现配置的经典池和集中流动性池，并获取代币元数据。
- 根据可执行池探测生成 Level 1 买卖报价，通过 WebSocket 更新并以轮询作为后备。
- 从链上兑换日志获取历史和实时逐笔成交，并据此构建周期 K 线。
- 使用可选 EVM 私钥签署即时市价兑换，并处理授权和可配置滑点。
- 获取钱包代币余额以及交易回执、订单状态和成交更新。
- 历史采集受配置的 Optimism 区块范围和区块数量限制。
- 不提供中心化订单簿、挂单限价单、原子改单或撤单。
- Optimism RPC、代币单位、池类型、签名和事件日志均封装在标准 StockSharp API 之后。

## 适用场景

适用于 Optimism DEX 监控、Velodrome 池分析、事件回测、钱包跟踪和直接兑换执行。

池覆盖、可执行价格、流动性、RPC 历史、Gas 成本、交易终局性和端点可用性取决于 Velodrome、Optimism 及配置的 RPC 服务。
