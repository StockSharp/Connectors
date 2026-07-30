# Conector Coincall
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Coincall** integra o StockSharp a opções e futuros da Coincall. A configuração de produto seleciona a superfície de derivativos; REST fornece snapshots e histórico, enquanto sessões WebSocket autenticadas oferecem atualizações ao vivo e privadas.

## Principais recursos

- Descoberta de instrumentos de opções ou futuros da Coincall.
- Assinatura de Level 1, profundidade, negócios tick a tick e candles por período.
- Download de negócios recentes e candles históricos antes de continuar com atualizações WebSocket.
- Envio de ordens limitadas, a mercado e condicionais com preço de gatilho e parâmetros aceitos GTC, IOC, FOK, post-only e reduce-only.
- Modificação ou cancelamento de uma ordem e cancelamento de grupos correspondentes.
- Carregamento de saldos, posições, ordens abertas e históricas e negócios próprios.
- Reconciliação do estado privado em intervalo configurável.

## Uso típico

Use este conector para acompanhar derivativos e automatizar a negociação de opções ou futuros na Coincall. Descoberta e snapshots REST podem conectar sem credenciais, mas WebSocket e todas as operações privadas exigem chave e segredo de API.

Cada instância seleciona apenas uma superfície de produto. Ordens iceberg e vencimento absoluto não são aceitos; os livros são baseados em snapshots e não há log de ordens. Instrumentos, permissões de negociação e limites de API são controlados pela Coincall.
