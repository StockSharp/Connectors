# Conector OpenFIGI
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector OpenFIGI** conecta o StockSharp a um serviço de mapeamento de identificadores de instrumentos financeiros e dados de referência. Ele traduz resultados específicos do provedor para o modelo unificado de instrumentos do StockSharp, permitindo usar identificadores consistentes entre diferentes fontes de dados.

## Principais recursos

- Cobertura típica: instrumentos financeiros globais e metadados de identificadores.
- Mapeamento por FIGI, ISIN, CUSIP, SEDOL, ticker ou outro tipo de identificador OpenFIGI.
- Pesquisa e filtragem por código de bolsa, MIC, moeda, setor de mercado e tipo de instrumento.
- Mensagens normalizadas de instrumentos do StockSharp com dados de referência e identificadores do provedor.
- Este adaptador é somente leitura: não fornece fluxos de preços nem encaminha ordens.
- Transporte REST, paginação, limitação de solicitações e formatos de resposta específicos do provedor ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para manter dados mestres de instrumentos, enriquecer identificadores, reconciliar dados entre provedores e incorporar instrumentos aos fluxos do StockSharp.

Mapeamentos, resultados de pesquisa, tamanhos de página, limites e disponibilidade dependem de OpenFIGI e de uma chave de API estar configurada.
