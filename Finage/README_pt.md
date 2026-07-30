# Conector Finage
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Finage** conecta o StockSharp aos serviços de dados de mercado Forex da Finage. É um adaptador somente para leitura de instrumentos de câmbio que combina dados históricos e de referência por REST com um fluxo opcional de cotações por WebSocket.

## Principais recursos

- Descoberta de pares de moedas a partir de uma lista de símbolos configurada ou pela pesquisa de símbolos da API REST do provedor.
- Instantâneos atuais dos melhores preços de compra e venda por REST.
- Atualizações ao vivo dos preços de compra e venda de nível 1 por WebSocket quando um token de streaming separado está configurado.
- Candles históricos por REST de 1, 5, 10, 15 e 30 minutos; 1, 2, 4, 6, 8 e 12 horas; 1 dia; e 1 semana.
- Intervalo de solicitações e quantidade máxima de instrumentos configuráveis para controlar o uso do REST.
- Eventos históricos de nível 1 e atualizações de candles ao vivo não são compatíveis.
- Sem negócios por tick, livros de ofertas, envio de ordens, dados de carteira ou operações de conta.

## Uso típico

Use este conector em listas de observação Forex, monitoramento de cotações, gráficos, pesquisas e backtesting baseado no histórico de candles da Finage.

É necessária uma chave da API REST da Finage e, para cotações ao vivo, um token de streaming adicional. A cobertura de símbolos, a profundidade histórica, o acesso em tempo real e os limites de solicitações dependem do plano Finage contratado.
