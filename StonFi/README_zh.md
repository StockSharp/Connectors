# STON.fi 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**STON.fi 连接器**将 StockSharp 接入 STON.fi 流动性池和 TON 区块链。它把配置或发现的池表示为交易品种，并将兑换报价、池事件、钱包余额和已提交兑换转换为 StockSharp 消息。

## 主要功能

- 发现已配置的池或数量受限的热门 STON.fi 池，并获取代币元数据。
- 通过可执行兑换模拟并以轮询方式生成 Level 1 买卖报价。
- 从 TON 池事件获得历史和实时逐笔成交，并据此构建周期 K 线。
- 使用 TON Wallet V4 助记词、可配置滑点和 TON Center 广播提交即时市价兑换。
- 获取钱包代币余额并跟踪兑换订单和成交状态。
- 历史请求受配置的 TON 区块范围限制；实时传递依赖轮询。
- 不提供中心化订单簿、挂单限价单、改单或撤单。
- STON.fi REST 数据、TON 单位、钱包签名和链上事件均封装在标准 StockSharp API 之后。

## 适用场景

适用于 TON DEX 报价监控、池分析、基于兑换的策略、钱包跟踪和直接 STON.fi 执行。

池覆盖、报价质量、事件历史、路由、费用、交易终局性和服务可用性取决于 STON.fi、TON Center、配置的端点及链上状态。
