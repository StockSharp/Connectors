# Conector de MAX Exchange

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de MAX Exchange** conecta StockSharp con el mercado al contado taiwanés operado por MaiCoin Group. Resulta especialmente útil para mercados de criptomonedas en TWD y USDT.

## Funciones principales

- Descubrimiento de instrumentos spot con estado, precisión y mínimos de orden.
- Cotizaciones Level 1, libros Level 2, operaciones públicas y velas OHLCV.
- Tickers, libros, operaciones y velas en tiempo real mediante WebSocket.
- Velas históricas e instantáneas recientes mediante REST API v3.
- Saldos, órdenes abiertas e históricas y ejecuciones privadas.
- Órdenes market, limit, stop-market, stop-limit, post-only e IOC limit.
- Cancelación individual y masiva, con direcciones REST y WebSocket configurables.

## Uso habitual

Use este conector en robots de trading, terminales, recolectores de datos TWD y sistemas de supervisión y gestión de órdenes.

Los datos públicos no requieren credenciales. Las operaciones de cuenta y trading requieren la clave y el secreto de API de MAX Exchange.
