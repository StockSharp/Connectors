# Coinmetro 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Coinmetro 连接器**将 StockSharp 接入 Coinmetro 加密货币现货交易所。它将品种、账户、订单和 K 线 REST 端点与市场及私有活动的 WebSocket 实时更新结合，并支持独立的实盘和演示环境。

## 主要功能

- 发现 Coinmetro 现货品种及其交易约束。
- 通过 WebSocket 订阅实时 Level 1 行情、市场深度和逐笔成交。
- 下载 1 分钟、5 分钟、30 分钟、4 小时和日线周期的历史 K 线。
- 提交限价单和市价单，并使用支持的 GTC、IOC、FOK 和 GTD 参数。
- 撤销单个订单或一组匹配的活动订单。
- 加载余额、活动及历史订单和自身成交。
- 在可配置的实盘与演示 REST 和 WebSocket 端点之间切换。

## 适用场景

该连接器适合监控 Coinmetro 现货市场、加载 K 线历史并进行自动交易。实盘私有操作需要配置具备相应权限的访问令牌；演示模式使用独立的开放端点，并可自动获取演示令牌。

K 线仅提供历史数据，不会继续实时更新。适配器不支持原子改单、条件单、冰山单或只挂单，订单簿以快照而非 StockSharp 增量形式发布。设计策略时应考虑私有状态校准频率和 API 速率限制。
