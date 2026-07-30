# Pendle 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**Pendle 连接器**将 StockSharp 接入链上收益交易协议。它把协议数据和钱包操作转换为统一的 StockSharp 消息模型，使应用程序能够对 Pendle 市场使用标准订阅和交易工作流程。

## 主要功能

- 典型覆盖范围：链上生息资产、本金代币、收益代币和 Pendle 市场。
- 发现交易品种并获取协议参考数据。
- 适配器支持的市场数据：Level 1 行情和 K 线。
- 请求历史 K 线并持续更新市场数据，用于图表、分析和策略工作流程。
- 提供商支持的代币转换和区块链交易提交，包括必要的代币授权。
- 钱包投资组合、余额、持仓和成交状态更新。
- 协议特有的 HTTP 与 RPC 传输、钱包交易和数据格式均封装在标准 StockSharp API 之后。

## 适用场景

适用于收益市场监控、实时策略、感知钱包状态的交易工具，以及需要通过 Pendle 获取报价或执行转换的服务。

可用网络、市场、代币、报价、交易功能、费用和服务可用性取决于 Pendle、所配置的 API 与 RPC 端点、当前链上状况以及钱包权限。
