# Conector de Birdeye
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Birdeye** integra StockSharp con las API de datos de criptomonedas en cadena de Birdeye. Permite descubrir tokens y obtener indicadores actuales e históricos OHLCV para una blockchain seleccionada; Solana es la opción predeterminada.

## Funciones principales

- Descubrir tokens y cargar datos de referencia de la red seleccionada.
- Limitar la búsqueda por dirección del token y aplicar un filtro de liquidez mínima.
- Solicitar instantáneas de Level 1 y actualizarlas mediante consultas periódicas a REST.
- Descargar velas históricas por intervalo dentro del límite de historial configurado.
- Activar el WebSocket de pago para recibir actualizaciones en directo de Level 1 y velas.
- Expresar los precios en dólares estadounidenses o en la moneda nativa de la red.
- Usar los intervalos admitidos por Birdeye; las velas inferiores a un minuto solo están disponibles en Solana.

## Uso habitual

Este conector resulta adecuado para filtrar tokens, vigilar precios en cadena y analizar históricos OHLCV en las redes compatibles con Birdeye. Antes de suscribirse, configure la red, el token de API, la moneda de cotización y los filtros opcionales.

Birdeye es un proveedor de datos, por lo que el conector no ofrece órdenes, carteras, ejecución de operaciones ni libro de órdenes. No hay eventos históricos de Level 1 y, sin el modo de streaming, la suscripción a velas finaliza después de entregar el historial. La cobertura, el acceso por WebSocket y los límites dependen del plan de API de Birdeye.
