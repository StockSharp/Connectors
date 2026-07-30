# Conector de Coinalyze
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Coinalyze** integra StockSharp con la API de análisis del mercado de criptomonedas de Coinalyze. Convierte precios históricos e indicadores de derivados en velas estándar de StockSharp para instrumentos de futuros o contado.

## Funciones principales

- Seleccionar instrumentos de futuros o contado y limitar opcionalmente el descubrimiento por bolsa.
- Descargar velas históricas de precio, interés abierto, tasa de financiación, liquidaciones o relación entre posiciones largas y cortas.
- Utilizar los intervalos compatibles con la API de Coinalyze.
- Convertir opcionalmente a dólares los valores de interés abierto y liquidaciones.
- Configurar un límite de hasta 2.000 registros históricos por solicitud.
- Autenticar las solicitudes con un token de API de Coinalyze.

## Uso habitual

Use este conector para backtesting, investigación de derivados y análisis comparativo de métricas históricas de Coinalyze. Seleccione el tipo de mercado y la métrica antes de suscribirse, y aplique un filtro de bolsa cuando necesite reducir el universo.

El adaptador es histórico y funciona solo por REST. No ofrece velas en directo, Level 1, operaciones tick a tick, profundidad, carteras ni ejecución de órdenes. Los símbolos, intervalos, profundidad histórica y frecuencia de solicitudes dependen de la API de Coinalyze.
