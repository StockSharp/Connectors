# Coinstore 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Coinstore 连接器**将 StockSharp 接入 Coinstore 加密货币现货市场，适合跟踪交易所丰富的上币市场，并自动交易加密货币与稳定币交易对。

## 主要功能

- 获取现货品种、交易状态、价格和数量精度以及最小下单参数。
- Level 1、Level 2 订单簿、公开成交和 OHLCV K 线。
- 通过 WebSocket 实时订阅行情、深度、成交和 K 线。
- 通过 REST 获取近期成交、订单簿快照和 K 线历史。
- 账户余额、活动订单、订单状态和私有成交。
- 市价、限价、Post-only 和 IOC 订单，以及单笔和批量撤单。
- 可配置 REST 与 WebSocket 服务地址。

公开市场数据无需凭据。账户和交易功能需要 Coinstore API 密钥与 Secret。私有状态通过经过认证的 REST 请求刷新。
