# SSI 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**SSI 连接器**通过 SSI FastConnect API v3 将 StockSharp 接入越南证券市场。它把 SSI 的市场数据和经纪业务转换为统一的 StockSharp 消息模型。

## 主要功能

- 发现 HOSE、HNX 和 UPCOM 的证券与指数，包括股票和受支持的期货。
- 实时 Level 1 行情、逐笔成交和订单簿订阅，并在可用时先提供 REST 快照。
- 请求历史周期 K 线，并在受支持的周期上继续接收流式更新。
- 提交、替换和撤销单笔订单，包括 SSI 特有的订单条件。
- 查询账户，并通过流式通道和定期核对更新余额、持仓、订单与成交。
- 可配置 REST、WebSocket 端点和投资组合轮询间隔。
- 必须提供 FastConnect 凭据；交易还取决于 Client ID、账户、RSA 私钥和当前 OTP。
- SSI 特有的会话、载荷和流主题均封装在标准 StockSharp API 之后。

## 适用场景

适用于需要直接接入 SSI 经纪服务的越南市场终端、实时策略、订单管理服务和监控工具。

可用交易品种、历史深度、交易权限、请求限制和服务可用性由 SSI 及所连接 FastConnect 账户的权限决定。
