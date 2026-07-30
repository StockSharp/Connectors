# Conector Buda
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Buda** integra o StockSharp à bolsa de criptomoedas à vista Buda.com. Os dados públicos de mercado estão disponíveis sem credenciais, enquanto as operações REST autenticadas usam chave e segredo de API.

## Principais recursos

- Descoberta dos instrumentos à vista oferecidos pela Buda.
- Assinatura de cotações de Level 1, profundidade de mercado e negócios tick a tick.
- Combinação de atualizações públicas por WebSocket com snapshots e reconciliação por REST.
- Envio de ordens limitadas e a mercado e cancelamento individual ou em grupo.
- Carregamento de saldos, estado do portfólio, ordens ativas e históricas e negócios próprios.
- Reconciliação do estado privado em intervalo de consulta configurável.

## Uso típico

Use este conector para acompanhar o mercado à vista da Buda em tempo real e negociar de forma autenticada pelo StockSharp. Aplicações que usam somente dados públicos dispensam credenciais; ordens e dados da conta exigem chave e segredo da API Buda com as permissões necessárias.

O adaptador não fornece candles nem fluxo de log de ordens, e o livro de ofertas é entregue em snapshots, não em incrementos. A substituição atômica de ordens não é aceita; a estratégia deve cancelar a ordem anterior e enviar outra separadamente. As permissões e os limites da bolsa continuam válidos.
