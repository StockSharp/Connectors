# SimFin 连接器
[English](README.md) | [Русский](README_ru.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

**SimFin 连接器**让 StockSharp 以只读方式访问 SimFin 的公司基本面和日线价格历史。它把提供商记录映射为 StockSharp 证券、Level 1 快照、日 K 线和专用基本面消息类型。

## 主要功能

- 按股票代码或 SimFin 公司标识符发现公司和证券。
- 将最新可用的日价格记录作为 Level 1 快照。
- 获取历史日线 OHLCV；不支持盘中周期或实时 K 线更新。
- 获取可配置的损益表、资产负债表、现金流量表和衍生指标。
- 控制财务期间、日期范围、标准化或原始披露值、比率及最大记录数。
- 仅提供用于研究和历史采集的有限 REST 订阅；没有流式传输。
- 不提供逐笔成交、订单簿、新闻、投资组合或交易操作。
- SimFin 认证、节流和响应格式均封装在标准 StockSharp API 之后。

## 适用场景

适用于基本面筛选、估值研究、日线分析，以及将 SimFin 数据与其他连接器的成交或盘中数据结合的回测。

公司覆盖、报表字段、历史、更新频率、请求限制和访问权限由 SimFin 及所连接的 API 套餐决定。
