# Conector Coinmetro
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Coinmetro** integra o StockSharp à bolsa de criptomoedas à vista Coinmetro. Ele combina endpoints REST de instrumentos, conta, ordens e candles com atualizações WebSocket de mercado e atividade privada, e aceita ambientes separados de produção e demonstração.

## Principais recursos

- Descoberta de instrumentos spot da Coinmetro e suas restrições de negociação.
- Assinatura por WebSocket de Level 1, profundidade e negócios tick em tempo real.
- Download de candles históricos de 1, 5 e 30 minutos, 4 horas e um dia.
- Envio de ordens limitadas e a mercado com parâmetros aceitos GTC, IOC, FOK e GTD.
- Cancelamento de uma ordem ou de grupos de ordens abertas correspondentes.
- Carregamento de saldos, ordens abertas e históricas e negócios próprios.
- Alternância entre endpoints REST e WebSocket configuráveis de produção e demonstração.

## Uso típico

Use este conector para acompanhar o mercado à vista da Coinmetro, carregar histórico de candles e automatizar operações. Operações privadas reais exigem token de acesso com as permissões necessárias; o modo de demonstração usa endpoints abertos separados e pode obter seu token demo automaticamente.

Os candles são somente históricos e não continuam com atualizações ao vivo. Não há substituição atômica nem ordens condicionais, iceberg ou post-only, e os livros são publicados como snapshots, não incrementos StockSharp. Considere o intervalo de reconciliação privada e os limites de API no projeto da estratégia.
