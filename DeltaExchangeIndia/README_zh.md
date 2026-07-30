# Delta Exchange India 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Delta Exchange India 连接器**将 StockSharp 接入印度的中心化数字资产衍生品交易平台，并把期货与期权行情、订单及账户状态转换为标准的 StockSharp 消息模型。

## 主要功能

- 发现 Delta Exchange India 上市的期货和期权，并获取其参考数据。
- 通过 REST 获取 Level 1 快照，并通过 WebSocket 接收实时更新；不提供历史 Level 1 事件。
- 通过 REST 获取近期逐笔成交，每次请求最多 50 笔，并通过 WebSocket 接收实时成交。
- 获取订单簿快照和最多 15 档的实时更新；不支持增量订单簿和历史订单簿。
- 获取历史 K 线，每次请求最多 1,999 根，并接收提供商所支持周期的实时 K 线更新。
- 支持限价单、市价单和条件止损单，以及 post-only、reduce-only、改单、撤单和批量撤单。
- 通过认证 REST API 和私有数据流获取投资组合、余额、持仓、订单及成交更新。

## 适用场景

该连接器适用于实时衍生品策略、交易终端、订单管理服务，以及需要 Delta Exchange India 近期成交或 K 线历史的分析系统。

私有操作需要 API 凭据和相应账户权限。可用合约、历史数据范围、请求频率限制及地区可用性由提供商决定；目前未实现冰山订单、绝对到期时间和批量平仓。
