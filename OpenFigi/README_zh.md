# OpenFIGI 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**OpenFIGI 连接器**将 StockSharp 接入金融工具标识符映射和参考数据服务。它把提供商特有的结果转换为统一的 StockSharp 证券模型，使应用程序能够在不同数据源之间使用一致的交易品种标识符。

## 主要功能

- 典型覆盖范围：全球金融工具及其标识符元数据。
- 按 FIGI、ISIN、CUSIP、SEDOL、代码或其他 OpenFIGI 标识符类型进行映射。
- 按交易所代码、MIC、货币、市场板块和证券类型搜索及筛选交易品种。
- 生成包含提供商参考数据和标识符的标准化 StockSharp 证券消息。
- 此适配器为只读：不提供价格流，也不进行订单路由。
- 提供商特有的 REST 传输、分页、限流和响应格式均封装在标准 StockSharp API 之后。

## 适用场景

适用于维护证券主数据、补充标识符、核对不同提供商的数据，以及将交易品种接入 StockSharp 工作流程。

可用映射、搜索结果、分页大小、请求限制和服务可用性由 OpenFIGI 以及是否配置 API 密钥决定。
