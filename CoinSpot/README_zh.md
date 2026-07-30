# CoinSpot 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**CoinSpot 连接器**将 StockSharp 接入 CoinSpot 加密货币现货交易所和经纪服务。它使用 CoinSpot 的公开、交易和只读私有 REST API 获取市场数据、账户状态并执行订单操作。

## 主要功能

- 发现 CoinSpot 现货市场及品种资料。
- 获取 Level 1 行情快照、订单簿快照和近期逐笔成交。
- 按可配置的间隔通过 REST 轮询更新公开订阅。
- 提交限价或市价买卖订单。
- 撤销单个订单或一组匹配的活动订单。
- 加载余额、投资组合状态、活动及历史订单和自身成交。
- 分别配置公开、交易和只读私有 API 端点。

## 适用场景

该连接器适合监控 CoinSpot 现货市场并通过 REST 自动交易。公开市场数据无需身份验证；账户和订单操作需要具备相应权限的 CoinSpot API 密钥和密钥口令。

适配器没有 WebSocket 数据流，也不提供 K 线、历史 Level 1 事件或历史订单簿。公开数据通过轮询更新，近期成交历史受提供商响应范围限制。它不支持原子改单、条件单、冰山单、只挂单或 GTD 订单。
