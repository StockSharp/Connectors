# Samco 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Samco 连接器**通过 Samco Trade API 将 StockSharp 接入印度证券和衍生品市场。它以统一的 StockSharp 消息模型提供经纪商的行情与交易服务。

## 主要功能

- 发现 NSE、BSE、NFO、BFO、CDS、MCX 和 MFO 支持的股票、期货与期权。
- 通过 Samco 行情通道获取实时 Level 1、逐笔成交和五档订单簿。
- 获取历史周期 K 线，并通过流式通道或 REST 轮询继续更新。
- 提交和修改限价单及经纪商支持的订单，并可逐单撤销；不提供原子批量撤单。
- 获取资金限额、持仓资产、仓位、订单和成交，并定期核对私有状态。
- 可选 WebSocket 行情、REST 备用轮询以及可配置的轮询间隔和服务端点。
- 可使用现有的当日会话令牌或 Samco API 凭据认证，并受经纪商会话规则约束。
- Samco 特有的标识符、会话和载荷均封装在标准 StockSharp API 之后。

## 适用场景

适用于连接 Samco 账户的印度市场终端、实时策略、投资组合监控和订单管理应用。

交易品种、五档深度、历史数据、交易权限、限制和会话期限由 Samco 及所连接账户决定。
