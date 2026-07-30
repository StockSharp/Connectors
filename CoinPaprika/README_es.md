# Conector de CoinPaprika
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de CoinPaprika** integra StockSharp con la API de datos de criptomonedas de CoinPaprika. Ofrece datos de referencia globales de monedas o mercados de una bolsa seleccionada, además de instantáneas de ticker y velas históricas OHLCV.

## Funciones principales

- Descubrir monedas de CoinPaprika globalmente o limitar los instrumentos a una bolsa configurada.
- Elegir la moneda de cotización para las solicitudes de ticker y velas.
- Recibir instantáneas de Level 1 con precio, volumen de 24 horas, variación y estado cuando estén disponibles.
- Actualizar Level 1 mediante consultas REST con intervalo configurable.
- Descargar velas históricas OHLCV por intervalo.
- Usar la API gratuita sin token o configurar uno para el endpoint profesional y permisos ampliados.
- Limitar las respuestas históricas a un máximo configurable de 366 registros.

## Uso habitual

Use este conector para datos de referencia de criptomonedas, seguimiento ligero de precios e investigación histórica OHLCV. Elija entre descubrimiento global o por bolsa y establezca la moneda de cotización antes de solicitar datos.

CoinPaprika es un agregador de datos, no un centro de negociación. El adaptador no ofrece órdenes, carteras, operaciones tick a tick ni profundidad. No hay eventos históricos de Level 1 ni actualización en directo de velas. El historial intradía, la cobertura, el tamaño de respuesta y los límites dependen del plan y del token de CoinPaprika.
