# DexScreener 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**DexScreener 连接器**通过 DexScreener 的公共 REST API，将多链去中心化交易所交易对分析数据接入 StockSharp。该适配器只读取行情数据，无需 API 凭据。

## 主要功能

- 可按链标识、代币地址、精确交易对地址或自由文本搜索交易对，并支持 StockSharp 的跳过与数量限制。
- Level 1 快照包含最新美元价格、原生代币计价价格、24 小时成交量和价格变化、流动性及交易状态。
- 通过 REST 定期刷新活动的 Level 1 订阅；轮询间隔可配置，默认 30 秒。
- 覆盖 DexScreener 所索引的区块链和流动性池。
- 无需 API 密钥或私有账户会话即可公开访问。
- 不提供历史 Level 1 事件，也没有实时流式传输。
- 不支持逐笔成交、订单簿、K 线、下单、投资组合数据或账户操作。

## 适用场景

该连接器适用于 DEX 交易对发现、自选列表、流动性筛选和需要定期刷新聚合市场指标的监控面板。

它不是交易执行连接器，也不提供适合回测的事件历史。交易对覆盖范围、字段可用性、数据时效性和请求限制取决于 DexScreener 及相应的去中心化交易平台。
