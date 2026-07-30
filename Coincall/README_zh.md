# Coincall 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Coincall 连接器**将 StockSharp 接入 Coincall 期权和期货市场。产品设置用于选择衍生品接口；REST 提供快照和历史数据，经过认证的 WebSocket 会话则提供实时市场及私有更新。

## 主要功能

- 发现 Coincall 期权或期货品种。
- 订阅 Level 1 行情、市场深度、逐笔成交和指定周期 K 线。
- 加载近期成交和历史 K 线，然后继续接收 WebSocket 实时更新。
- 提交限价单、市价单和带触发价的条件单，并使用支持的 GTC、IOC、FOK、只挂单及只减仓参数。
- 修改或撤销单个订单，并批量撤销匹配订单。
- 加载余额、持仓、活动及历史订单和自身成交。
- 按可配置的间隔校准私有状态。

## 适用场景

该连接器适合监控 Coincall 衍生品并自动交易期权或期货。REST 品种发现和快照无需凭据也可连接，但 WebSocket 推送及所有私有操作都需要 API 密钥和密钥口令。

每个适配器实例只能选择一个产品接口。适配器不支持冰山单或绝对到期时间订单；订单簿以快照形式提供，且没有订单日志数据流。可用品种、交易权限和 API 限额由 Coincall 控制。
