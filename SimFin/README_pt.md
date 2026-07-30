# Conector SimFin
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector SimFin** dá ao StockSharp acesso somente leitura a fundamentos empresariais e histórico diário de preços da SimFin. Os registros são convertidos em instrumentos, snapshots de nível 1, candles diários e um tipo específico de mensagens fundamentais.

## Principais recursos

- Pesquisa de empresas e instrumentos por ticker ou identificador de empresa SimFin.
- Registro diário mais recente disponível como snapshot de nível 1.
- Candles OHLCV diários históricos; intervalos intradiários e atualizações ao vivo não são suportados.
- Demonstrativos configuráveis de resultados, balanço, fluxo de caixa e métricas derivadas.
- Controles de período fiscal, datas, valores padronizados ou reportados, indicadores e máximo de registros.
- Somente assinaturas REST finitas para pesquisa e histórico; não há streaming.
- Não fornece ticks, livros, notícias, carteiras nem operações de negociação.
- Autenticação, limitação de frequência e formatos SimFin ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para triagem fundamental, avaliação, análise diária e backtests que combinem SimFin com execução ou dados intradiários de outro conector.

Empresas, campos, histórico, frequência, limites e acesso dependem da SimFin e do plano de API conectado.
