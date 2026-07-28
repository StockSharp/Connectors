# Conector BigONE

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector BigONE** integra o StockSharp aos mercados spot e de contratos da BigONE. Um único adaptador cobre pares de cripto e contratos perpétuos com margem em moeda ou USDT.

## Principais recursos

- Descoberta de pares spot e contratos perpétuos disponíveis.
- Cotações Level 1, livros de ofertas, negócios públicos e candles OHLCV.
- Fluxos spot por JSON WebSocket e fluxos URL dedicados para contratos.
- Histórico de candles spot e snapshots REST atuais dos dois mercados.
- Saldos spot e de contratos, posições, ordens e negócios privados.
- Ordens market, limit, IOC, FOK, post-only, stop spot e reduce-only de contratos.
- Cancelamento individual e em grupo.
- Endereços configuráveis para REST spot/contratos e WebSocket público e privado.

## Uso

Use o conector em robôs, terminais, coletores de dados, monitoramento e gestão de ordens que combinem a liquidez spot e os derivativos da BigONE.

Dados públicos não exigem credenciais. Conta e negociação exigem uma chave de API e um segredo da BigONE.
