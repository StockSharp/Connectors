# WazirX 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**WazirX 连接器**将 StockSharp 接入 WazirX 中心化加密货币现货交易所。它把交易所 REST、WebSocket 数据及账户操作转换为统一的 StockSharp 消息模型。

## 主要功能

- 发现现货市场及其价格、数量步长和交易规则元数据。
- 通过公共流获取实时 Level 1、逐笔成交、订单簿和周期 K 线。
- 在转入实时更新前提供 REST 快照以及可用的历史成交和 K 线。
- 提交限价单和受支持的止损限价单，逐单或按条件批量撤销，并更新订单和成交状态。
- 通过私有流和 REST 核对更新余额与投资组合。
- 私有操作需要 API 密钥和 Secret；公共行情无需交易凭据。
- 本适配器不提供市价单和原子改单。
- WazirX 认证、代码、传输、过滤规则和载荷均封装在标准 StockSharp API 之后。

## 适用场景

适用于 WazirX 现货终端、实时策略、图表、账户监控和订单管理服务。

可用市场、历史深度、止损限价支持、交易权限、过滤规则、请求限制和服务可用性由 WazirX 及所连接账户决定。
