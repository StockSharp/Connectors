# Velora 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Velora 连接器**将 StockSharp 接入 Velora Market API 和受支持的 EVM 网络。它把配置的代币对表示为交易品种，并将可执行价格、钱包余额和路由兑换映射为 StockSharp 消息。

## 主要功能

- 在 Ethereum、Optimism、BNB Chain、Gnosis、Polygon、Base、Arbitrum 和 Avalanche 上发现配置的代币对。
- 通过轮询可执行 Velora 路由报价生成 Level 1 买卖价格。
- 通过所选网络的 JSON-RPC 端点构建、签署并广播即时市价兑换。
- 支持可选自动代币授权，并可配置滑点、探测数量和回执超时。
- 获取钱包代币余额并跟踪交易回执、订单状态和成交。
- 可配置 Velora 合作伙伴标识、钱包地址、代币对、API 和 RPC 端点。
- 不提供逐笔成交、订单簿、K 线、历史行情、挂单、改单或撤单。
- Velora 路由、代币单位、授权、签名和 EVM 回执均封装在标准 StockSharp API 之后。

## 适用场景

适用于跨代币报价监控、钱包看板，以及在受支持 EVM 网络上直接执行 Velora 路由兑换。

交易对覆盖、路由、流动性、价格影响、Gas 成本、授权、终局性和限制取决于 Velora、所选网络及 RPC 提供商。
