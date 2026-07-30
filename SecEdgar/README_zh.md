# SEC EDGAR 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**SEC EDGAR 连接器**让 StockSharp 以只读方式访问美国证券交易委员会发布的官方申报数据。它把发行人、申报文件和 XBRL 公司事实映射为 StockSharp 的证券、新闻和专用基本面消息类型。

## 主要功能

- 使用 SEC 公司代码目录按股票代码或 CIK 查找公司。
- 将申报文件作为 StockSharp 新闻获取，包括近期申报和可配置数量的历史提交文件。
- 按 10-K、10-Q、8-K、20-F、40-F、6-K 等表单类型过滤。
- 通过专用 Company Facts 数据类型获取 XBRL 公司事实，并支持日期和记录数筛选。
- 适合历史采集和定期刷新的有限 REST 请求；适配器不会建立推送流。
- 不需要 API 密钥，但 SEC 政策要求提供可识别的 User-Agent 并控制请求频率。
- 不提供价格、成交、订单簿、K 线、投资组合或下单功能。
- SEC 端点、CIK 处理、历史文件和响应格式均封装在标准 StockSharp API 之后。

## 适用场景

适用于申报监控、基本面研究管道、发行人筛选，以及将 SEC 披露与其他连接器行情合并的数据集。

覆盖范围和时效取决于 SEC 发布的数据；请求节奏、历史文件数、事实数和表单过滤由适配器设置及 SEC 访问政策控制。
