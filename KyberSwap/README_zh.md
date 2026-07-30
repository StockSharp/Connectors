# KyberSwap 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**KyberSwap 连接器**将 StockSharp 接入 KyberSwap Aggregator API v1 和 EVM 网络。它把已配置的代币对作为 StockSharp 品种，根据聚合器路由生成可执行报价，并提交已签名的链上兑换。

## 主要功能

- 在 Ethereum、Optimism、BNB Chain、Polygon、Base、Arbitrum、Avalanche 和 Linea 上发现已配置的代币对并加载代币元数据。
- 根据可执行聚合路由和可配置的探测数量计算 Level 1 买卖报价。
- 通过 REST 定期轮询活动的 Level 1 订阅；不提供历史报价事件或流式传输。
- 在本地签名并通过 EVM JSON-RPC 广播即时市价兑换，可配置滑点并自动进行代币授权。
- 通过链上调用获取钱包代币余额和投资组合更新。
- 按交易哈希跟踪连接器提交的兑换，直至 EVM 回执确认成功或失败。
- 不支持逐笔成交、订单簿、K 线、限价单，以及对已广播交易的修改或撤销。

## 适用场景

该连接器适用于在所支持 EVM 网络上监控考虑路由的 DEX 报价并自动执行市价兑换。

查询报价无需交易凭据，但执行需要钱包、私钥和可用的 RPC 端点。代币定义、路由流动性、授权额度、Gas 成本、滑点、回执延迟、API 限制和网络状态都会影响每次兑换的结果。
