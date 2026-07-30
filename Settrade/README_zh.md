# Settrade 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Settrade 连接器**通过 Settrade Open API v2 将 StockSharp 接入泰国股票和衍生品市场。它把提供商的 REST、MQTT 行情和经纪服务统一为 StockSharp 消息模型。

## 主要功能

- 对已配置的 SET 股票账户或 TFEX 衍生品账户按指定代码查询；不提供完整品种目录下载。
- 实时 Level 1 行情和订单簿快照与更新；不提供逐笔成交订阅。
- 获取历史周期 K 线，并在受支持周期上继续接收 MQTT 更新。
- 支持市价单、限价单和部分 TFEX 条件单；股票账户不提供止损单。
- 修改和撤销订单，并在适用时使用 Settrade 的有效期、NVDR、冰山、仓位和触发字段。
- 通过快照、私有主题和定期核对获取账户信息、投资组合、持仓、订单和成交。
- 可配置生产和沙盒端点；按功能需要提供凭据、Broker ID、账户类型、账户和交易 PIN。
- Settrade 认证、MQTT 主题和载荷均封装在标准 StockSharp API 之后。

## 适用场景

适用于通过 Settrade 接入的泰国市场终端、实时策略、订单管理服务和账户监控工具。

可用代码、K 线周期、订单簿深度、账户功能、交易权限和请求限制由 Settrade、所选账户类型及其授权决定。
