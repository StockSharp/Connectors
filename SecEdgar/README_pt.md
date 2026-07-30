# Conector SEC EDGAR
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector SEC EDGAR** dá ao StockSharp acesso somente leitura aos dados oficiais de registros da Comissão de Valores Mobiliários dos Estados Unidos. Emissores, documentos e fatos XBRL são convertidos em instrumentos, notícias e um tipo específico de mensagens fundamentais do StockSharp.

## Principais recursos

- Pesquisa de empresas por ticker ou CIK usando o catálogo de tickers da SEC.
- Registros como notícias do StockSharp, incluindo envios recentes e uma quantidade configurável de arquivos históricos.
- Filtros de formulários como 10-K, 10-Q, 8-K, 20-F, 40-F e 6-K.
- Fatos XBRL de empresas com filtros de data e quantidade pelo tipo Company Facts.
- Solicitações REST finitas para coleta histórica e atualização periódica; o adaptador não abre stream push.
- Não exige chave API, mas a política da SEC requer User-Agent identificável e ritmo adequado de solicitações.
- Não fornece preços, negócios, livros, candles, carteiras nem envio de ordens.
- Endpoints, CIKs, arquivos históricos e formatos da SEC ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para monitorar registros, pesquisar fundamentos, selecionar emissores e criar conjuntos que combinem divulgações da SEC com dados de outro conector.

Cobertura e pontualidade dependem das publicações da SEC; ritmo, limites de arquivos e fatos e filtros dependem das configurações e da política de acesso.
