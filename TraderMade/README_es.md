# Conector de TraderMade
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de TraderMade** conecta StockSharp con los servicios de datos de divisas y criptomonedas de TraderMade. Convierte el historial REST y las cotizaciones WebSocket al modelo unificado de mercado de StockSharp.

## Funciones principales

- Descubrimiento de pares desde la lista de divisas y las monedas de cotización configuradas, o desde una lista explícita de símbolos.
- Precios bid, ask y medio de nivel 1 en tiempo real mediante la API de streaming.
- Datos opcionales de libro TraderLadder cuando la cuenta dispone de acceso y se habilita la función.
- Velas históricas por intervalo mediante REST, con datos opcionales de criptomonedas durante fines de semana.
- Claves REST y de streaming separadas para configuraciones solo históricas, solo en vivo o combinadas.
- Las suscripciones de velas son históricas y finitas; no admite velas en vivo ni operaciones tick a tick.
- Es un conector exclusivo de datos, sin carteras, saldos ni envío de órdenes.
- Los símbolos, transportes y formatos de TraderMade quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para paneles de divisas y criptomonedas, cotizaciones en vivo, gráficos, análisis y backtests sin ejecución de bróker.

Los pares, TraderLadder, intervalos, historial, límites, datos de fin de semana y streaming dependen de TraderMade y del plan API.
