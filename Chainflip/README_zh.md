# Chainflip 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Chainflip 连接器**将 StockSharp 接入 Chainflip 跨链流动性网络。它将公开的 State Chain 与兑换服务数据结合，并可配置钱包，以便通过 StockSharp 交易模型提交跨链兑换。

## 主要功能

- 发现 Chainflip 支持的流动性池和资产。
- 接收由池状态和成交生成的 Level 1、池深度及逐笔成交数据。
- 配置 State Chain、报价服务、Ethereum 和 Arbitrum 端点。
- 请求报价，并将市价单提交为带保护参数的跨链兑换。
- 跟踪已提交的兑换，并通过投资组合消息提供钱包余额。
- 为支持链上的资产配置目标地址。

## 适用场景

该连接器适合监控 Chainflip 流动性，或从已配置的钱包执行即时跨链兑换。公开市场数据无需签名密钥；执行兑换则需要钱包地址、私钥、目标地址以及可用的链端点。

这是协议集成，并非中心化交易所的订单接口。适配器不提供 K 线、限价单、条件单或挂单。兑换交易广播后无法撤销、修改或批量取消。网络手续费、最终确认、流动性、滑点和各链可用性都会影响执行结果。
