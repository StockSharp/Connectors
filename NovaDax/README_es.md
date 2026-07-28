# Conector NovaDAX

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector NovaDAX** integra StockSharp con el mercado spot de criptomonedas de NovaDAX. El enfoque de la bolsa en pares con el real brasileño lo hace útil para supervisar el mercado, recopilar datos y automatizar operaciones en el mercado cripto de Brasil.

## Funciones principales

- Descubrimiento de instrumentos spot con estado de negociación, precisión de precio y cantidad y límites mínimos de orden.
- Cotizaciones Level 1, libros Level 2, operaciones públicas e historial de velas OHLCV.
- Ticker, profundidad y operaciones en tiempo real mediante Socket.IO.
- Instantáneas de mercado, operaciones recientes y velas históricas mediante REST.
- Saldos, órdenes activas e históricas, estado de órdenes y ejecuciones privadas.
- Órdenes de mercado, límite, stop-market y stop-limit con cancelación individual y por instrumento.
- Direcciones REST y Socket.IO, identificador de subcuenta y versión de Engine.IO configurables.

Los datos públicos están disponibles sin credenciales. Las funciones de cartera y negociación requieren una clave API y un secreto de NovaDAX; se puede indicar una subcuenta cuando sea necesario.
