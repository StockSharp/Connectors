# Conector de STON.fi
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de STON.fi** conecta StockSharp con los pools de liquidez de STON.fi y la blockchain TON. Representa los pools configurados o descubiertos como instrumentos y traduce cotizaciones de swaps, eventos, saldos y swaps enviados a mensajes de StockSharp.

## Funciones principales

- Descubrimiento de pools configurados o de un conjunto limitado de pools populares de STON.fi, con metadatos de tokens.
- Precios de compra y venta de nivel 1 calculados mediante simulaciones ejecutables y actualizados por sondeo.
- Operaciones tick a tick históricas y en vivo a partir de eventos de pools TON, con velas construidas desde esos swaps.
- Swaps de mercado inmediatos mediante una mnemónica de TON Wallet V4, deslizamiento configurable y difusión por TON Center.
- Saldos de tokens de la cartera y seguimiento del estado de orden y ejecución del swap.
- El historial está limitado por el rango de bloques TON configurado; la entrega en vivo depende del sondeo.
- No hay libro centralizado, órdenes limitadas en espera, sustitución ni cancelación.
- Los datos REST, las unidades TON, la firma y los eventos quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para supervisión de cotizaciones de DEX en TON, análisis de pools, estrategias basadas en swaps, seguimiento de carteras y ejecución directa en STON.fi.

La cobertura, las cotizaciones, el historial, las rutas, las comisiones, la finalidad y la disponibilidad dependen de STON.fi, TON Center, los endpoints y la blockchain.
