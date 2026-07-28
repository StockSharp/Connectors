# Quidax 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Quidax 连接器**将 StockSharp 接入 Quidax 现货交易所。它特别适合监控和交易以 NGN 及其他非洲法币计价的加密市场，也支持币币交易对。

## 主要功能

- 获取现货交易品种及其币种组成、价格和数量精度、最小订单金额。
- Level 1 行情、Level 2 订单簿、公开成交和历史 K 线。
- 通过可配置间隔的 REST 轮询持续订阅市场数据。
- 钱包余额、当前与历史订单以及私有成交。
- 限价单和市价单、单笔撤单及带筛选条件的批量撤单。
- 可配置 REST 服务地址、账户或子账户标识和轮询间隔。

公开市场数据无需凭据。查询投资组合和交易需要 Quidax 密钥。默认用户标识 `me` 指向令牌所有者，也可以替换为受支持的子账户标识。
