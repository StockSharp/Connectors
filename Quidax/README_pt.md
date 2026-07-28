# Conector Quidax

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Quidax** integra o StockSharp à bolsa spot Quidax. Ele é especialmente útil para acompanhar e negociar criptomoedas cotadas em NGN e outras moedas fiduciárias africanas, além de pares entre criptomoedas.

## Principais recursos

- Descoberta de instrumentos spot com composição do par, precisão de preço e volume e valor mínimo de ordem.
- Cotações de nível 1, livros de ofertas de nível 2, negócios públicos e candles históricos.
- Assinaturas contínuas de dados por consultas REST com intervalo configurável.
- Saldos de carteiras, ordens abertas e históricas e execuções privadas.
- Ordens limitadas e a mercado, cancelamento individual e cancelamento em grupo com filtros.
- Endereço REST, identificador de conta ou subconta e intervalo de consulta configuráveis.

Os dados públicos estão disponíveis sem credenciais. As funções de carteira e negociação exigem uma chave secreta da Quidax. O identificador padrão `me` aponta para o proprietário do token e pode ser substituído por um identificador de subconta compatível.
