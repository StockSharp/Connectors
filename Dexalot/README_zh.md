# Dexalot 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Dexalot 连接器**将 StockSharp 接入基于 Avalanche 的 Dexalot L1 链上中央限价订单簿。它结合公共 REST、WebSocket 数据和 EVM 合约调用，用于现货交易及账户状态获取。

## 主要功能

- 发现 Dexalot 现货代币交易对并获取参考数据。
- 通过合约读取 Level 1 和订单簿快照，再通过 WebSocket 接收实时更新；不提供历史 Level 1 和订单簿事件。
- 通过 WebSocket 获取成交和 K 线流，可在提供商给出的历史范围内按日期和数量筛选，并持续接收实时数据。
- 支持 5、15、30 分钟，1、4 小时和 1 日 K 线。
- 链上限价单和市价单，包括 post-only 行为及可配置的自成交防护。
- 支持改单、单笔撤单和批量撤单；不支持冰山订单、绝对到期时间和批量平仓。
- 通过 REST、WebSocket 和 EVM RPC 获取投资组合代币余额、订单与成交历史，并校准私有状态。

## 适用场景

该连接器适用于需要 Dexalot 订单簿和链上执行能力的实时现货策略、交易终端和订单管理服务。

交易需要钱包地址和私钥，并会产生网络 Gas 费用及确认延迟。可用交易对、流数据回补范围、API 限制、合约可用性和最终确认取决于 Dexalot 及所选网络端点。
