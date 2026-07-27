# Dukascopy JForex 连接器

[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Dukascopy JForex 连接器**通过官方 Java JForex SDK 将 StockSharp 连接到 Dukascopy Bank。SDK 负责与 Dukascopy 交易服务器建立安全的认证会话，.NET 适配器则通过仅限本机的桥接进程交换命令和事件。

## 主要功能

- 查询账户可用的外汇、CFD、贵金属、指数、商品和债券等证券。
- Level 1 报价、逐笔成交、订单簿更新和周期蜡烛图。
- 通过 JForex 历史服务获取历史逐笔和蜡烛图。
- 市价、限价、止损、止损限价以及 JForex 专用订单命令。
- 订单提交、修改、撤销，以及成交、余额和持仓更新。
- 可分别配置模拟和实盘 JForex 服务地址。
- 可从指定的可执行 JAR 启动桥接，也可将其作为独立本地进程管理。

## 运行方式

Dukascopy 将 JForex 作为 Java API 发布和维护，因此运行时需要 Java。随附的 Maven 桥接项目使用官方 `DDS2-jClient-JForex` 包。桥接仅监听本机 loopback 接口，不会把账户凭据暴露到网络。

该连接器适用于通过标准 StockSharp 消息模型访问 Dukascopy 的交易机器人、终端、监控和订单管理服务。可用证券、历史范围、市场深度和交易权限取决于 Dukascopy 账户。
