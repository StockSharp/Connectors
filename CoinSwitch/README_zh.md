# CoinSwitch 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**CoinSwitch 连接器**将 StockSharp 接入 CoinSwitch PRO API。产品设置可选择 INR 或 USDT 现货市场、USDT 保证金永续期货，或处于私有测试阶段的 HFT 期权接口。

## 主要功能

- 发现所选 CoinSwitch 产品的交易品种。
- 订阅 Level 1 行情、市场深度、逐笔成交和指定周期 K 线。
- 下载 K 线历史，并在所选产品和周期支持时通过 WebSocket 接收实时更新。
- 提交现货限价单、期货限价单、市价单或止损市价单，以及 HFT 期权限价单或市价单。
- 对支持的衍生品订单使用只减仓参数，并为 HFT 期权使用支持的有效方式。
- 撤销单个订单或一组匹配订单。
- 加载余额、持仓、活动及历史订单和自身成交。

## 适用场景

该连接器适合在一个所选产品接口上监控 CoinSwitch PRO 市场并进行自动交易。私有操作需要具备适当权限的 API 密钥和 Ed25519 密钥；期权还需要 CoinSwitch HFT 私有测试资格。

功能因产品而异：现货只支持限价下单；条件下单仅在期货中以止损市价单实现；期权 K 线不使用 WebSocket 推送。适配器不支持原子改单、冰山单、GTD 订单、增量订单簿或订单日志数据流。CoinSwitch 权限和速率限制仍然适用。
