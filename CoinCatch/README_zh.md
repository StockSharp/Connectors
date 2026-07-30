# CoinCatch 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**CoinCatch 连接器**将 StockSharp 接入 CoinCatch 现货和衍生品市场。产品设置可选择现货、USDT 保证金期货或币本位期货，REST 与 WebSocket API 则提供市场数据和认证交易。

## 主要功能

- 发现所选 CoinCatch 产品的交易品种。
- 订阅 Level 1 行情、市场深度、逐笔成交和指定周期 K 线。
- 下载历史 K 线，并通过 WebSocket 继续接收实时更新。
- 提交限价单和市价单，包括期货只减仓参数和限价单只挂单参数。
- 撤销单个订单或某一品种的全部订单。
- 加载余额、持仓、活动及历史订单和自身成交。
- 使用 API 密钥、密钥口令和密码短语进行身份验证并校准私有状态。

## 适用场景

该连接器适合监控 CoinCatch 现货或期货市场、获取 K 线历史并进行自动交易。连接前请选择产品；私有操作需要具备相应读取或交易权限的凭据。

适配器不提供 CoinCatch 计划单或触发单、冰山订单，也不支持原子改单。订单簿以快照形式提供，且没有订单日志数据流。使用时必须遵守品种规则、账户模式、API 权限和交易所速率限制。
