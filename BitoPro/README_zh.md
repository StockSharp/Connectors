# BitoPro 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**BitoPro 连接器**将 StockSharp 连接到面向台湾市场、受监管的加密货币交易所 BitoPro，并支持活跃的 TWD 现货市场。

## 主要功能

- 获取现货交易对、价格与数量精度以及交易限制。
- Level 1 行情、Level 2 订单簿快照和公开成交。
- 通过 WebSocket 实时接收 ticker、订单簿和成交。
- 获取 BitoPro 所有支持周期的历史 OHLCV K 线。
- 查询余额、当前与历史订单以及用户成交记录。
- 支持限价、市价、止损限价和 post-only 订单，以及单笔和批量撤单。
- 可配置 REST 与 WebSocket 服务地址。

## 典型用途

适用于交易机器人、终端、TWD 市场数据采集器、监控系统和订单管理服务。

公开市场数据无需凭据。账户和交易功能需要账户邮箱、API 密钥和密钥 Secret。BitoPro 的市价买单使用计价货币金额，因此连接器会按最新公开价格换算 StockSharp 的基础资产数量。
