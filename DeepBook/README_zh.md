# DeepBook 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**DeepBook 连接器**将 StockSharp 接入 Sui 上的 DeepBook 流动性协议。它结合公开 DeepBook 索引器和 Sui 全节点 gRPC 端点，提供流动性池数据、钱包余额和本地签名的即时兑换。

## 主要功能

- 发现 DeepBook 流动性池，并可按池名称、ID 或证券代码筛选。
- 获取 Level 1 快照、订单簿深度以及历史或轮询更新的逐笔成交。
- 下载并轮询从 1 分钟到 7 天周期的 K 线。
- 配置索引器、Sui 全节点、程序包、时钟对象、深度、历史上限和轮询参数。
- 配置钱包地址后，将 Sui 代币余额作为 StockSharp 投资组合提供。
- 将市价单提交为本地签名且带可配置滑点保护的 DeepBook 兑换。
- 跟踪生成的 Sui 交易摘要和兑换执行结果。

## 适用场景

该连接器适合监控 DeepBook 流动性池、采集 Sui DEX 市场数据，或从已配置钱包执行即时兑换。公开数据不需要私钥；投资组合数据需要钱包地址，兑换执行还需要对应的 Ed25519 签名密钥。

交易接口表示即时兑换，而不是 DeepBook 挂单。它不提供限价单、条件单、只挂单或有效时间参数；已执行的 Sui 交易也不能撤销、修改或批量取消。轮询延迟、索引器覆盖、滑点、Gas、流动性和 Sui 最终确认都会影响结果。
