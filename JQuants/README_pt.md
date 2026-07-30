# Conector J-Quants
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector J-Quants** conecta o StockSharp à J-Quants API V2 para dados históricos e de referência do mercado japonês. É um adaptador REST somente para leitura voltado à pesquisa, não ao streaming de mercado ao vivo ou à negociação.

## Principais recursos

- Descoberta e dados de referência de ações, futuros e opções japoneses, incluindo subjacentes, preços de exercício, tipos de opção e vencimentos dos derivativos.
- Uma mensagem de nível 1 gerada a partir da barra diária mais recente disponível; não é uma assinatura de cotações ao vivo.
- Negócios históricos por tick para ações; o histórico de ticks não está disponível para futuros ou opções.
- Candles históricos de ações de 1, 5, 15 e 30 minutos; 1 hora; e 1 dia.
- Candles históricos diários de futuros e opções.
- Intervalo configurável entre chamadas REST e profundidade máxima de paginação.
- Sem livros de ofertas, atualizações ao vivo, envio de ordens, dados de carteira ou operações de conta.

## Uso típico

Use este conector em catálogos de instrumentos japoneses, pesquisas históricas, gráficos, preparação de dados e backtesting com conjuntos de dados da J-Quants.

É necessária uma chave da J-Quants API V2. Endpoints, instrumentos, períodos, paginação e frequência de solicitações disponíveis dependem do plano contratado; os valores de nível 1 vêm de uma barra diária e não devem ser tratados como preços de compra e venda em tempo real.
