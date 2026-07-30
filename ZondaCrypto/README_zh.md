# Zonda Crypto 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Zonda Crypto 连接器**将 StockSharp 接入 zondacrypto 中心化加密货币现货交易所。它把交易所 REST、WebSocket 数据及账户操作转换为统一的 StockSharp 消息模型。

## 主要功能

- 发现现货市场及其币种、价格步长、数量步长和最小金额元数据。
- 通过公共流获取实时 Level 1、逐笔成交以及订单簿快照和更新。
- 在转入实时更新前提供 REST 快照和可用的近期成交历史；不提供 K 线。
- 提交市价单和限价单，并支持 GTC、IOC、FOK 及 post-only 选项。
- 逐单或按条件批量撤销，并更新订单和成交状态；不支持原子改单。
- 通过私有流和定期 REST 核对更新钱包余额与投资组合。
- 私有操作需要 API 密钥和 Secret；公共行情无需交易凭据。
- zondacrypto 认证、市场代码、传输、过滤规则和载荷均封装在标准 StockSharp API 之后。

## 适用场景

适用于 zondacrypto 现货终端、实时策略、近期成交分析、账户监控和订单管理服务。

可用市场、近期历史深度、交易权限、订单选项、请求限制和服务可用性由 zondacrypto 及所连接账户决定。
