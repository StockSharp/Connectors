# XRPL 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**XRPL 连接器**将 StockSharp 接入 XRP Ledger 内置的去中心化交易所。它把配置的货币对、账本订单簿、已成交 Offer、账户余额和签名交易映射为 StockSharp 消息。

## 主要功能

- 发现配置的 XRP 与发行代币交易对，并可选择许可型 DEX 域。
- 获取 Level 1 行情和可配置深度的订单簿快照，并随账本持续更新。
- 从订单簿变化生成历史和实时逐笔成交，并按账本活动构建周期 K 线。
- 支持限价 Offer、带价格保护的 IOC 市价 Offer、逐单撤销、替换和受跟踪的批量撤销。
- 获取账户余额、未成交 Offer、订单状态、成交、费用和交易状态。
- 公共行情只需 RPC 与 WebSocket 端点；交易需要经典账户地址和 family seed。
- 历史采集受账本扫描上限限制，实时快照使用配置的轮询间隔。
- XRPL 数量、发行方、签名、账本序列、费用和事件格式均封装在标准 StockSharp API 之后。

## 适用场景

适用于 XRPL DEX 终端、账本市场分析、历史研究、账户监控和直接 Offer 执行。

交易对覆盖、订单簿流动性、保留的账本历史、交易费用、终局性、许可域访问和端点可用性取决于 XRPL 网络状态及配置的服务。
