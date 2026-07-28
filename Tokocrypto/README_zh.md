# Tokocrypto 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Tokocrypto 连接器**将 StockSharp 接入 Tokocrypto 的 MAIN 现货市场。它适用于面向印度尼西亚市场的加密货币交易，以及需要使用 StockSharp 消息模型处理 Tokocrypto 行情的应用。

## 主要功能

- 获取 MAIN 现货交易品种及价格步长、数量步长和最小订单限制。
- Level 1 行情、Level 2 订单簿、公开成交和 OHLCV K 线。
- 通过 WebSocket 接收实时行情、部分深度、成交和 K 线。
- 通过公共 REST API 获取历史 K 线和最新市场快照。
- 查询现货余额、当前与历史订单以及个人成交记录。
- 支持市价、限价、止损市价、止损限价、post-only、IOC 和 FOK 订单。
- 支持单笔和批量撤单；账户 REST、行情 REST 和 WebSocket 地址均可配置。

## 典型用途

该连接器可用于 Tokocrypto 现货交易机器人、终端、行情采集器、监控服务和订单管理系统。

公共行情无需凭据。账户查询和交易操作需要 Tokocrypto API 密钥和 Secret。
