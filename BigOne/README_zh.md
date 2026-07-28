# BigONE 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**BigONE 连接器**将 StockSharp 接入 BigONE 现货和合约市场。一个消息适配器即可处理普通加密货币交易对，以及币本位或 USDT 本位永续合约。

## 主要功能

- 获取现货交易对和可用永续合约。
- Level 1 行情、订单簿、公开成交和 OHLCV K 线。
- 通过 JSON WebSocket 接收现货流，并通过独立 URL 接收合约流。
- 现货 K 线历史及两类市场的最新 REST 快照。
- 现货与合约余额、合约持仓、订单和私有成交记录。
- 市价、限价、IOC、FOK、post-only、现货止损和合约 reduce-only 订单。
- 单笔及批量撤单。
- 可配置现货/合约 REST、公开 WebSocket 和私有 WebSocket 地址。

## 使用场景

适用于结合 BigONE 现货流动性和衍生品的交易机器人、终端、行情采集、监控及订单管理系统。

公开行情无需凭据。账户查询和交易需要 BigONE API 密钥与 Secret。
