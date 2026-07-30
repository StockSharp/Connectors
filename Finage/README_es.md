# Conector de Finage
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Finage** conecta StockSharp con los servicios de datos de mercado Forex de Finage. Es un adaptador de solo lectura para instrumentos de divisas que combina datos históricos y de referencia por REST con un flujo opcional de cotizaciones por WebSocket.

## Funciones principales

- Descubrimiento de pares de divisas desde una lista de símbolos configurada o mediante la búsqueda de símbolos de la API REST del proveedor.
- Instantáneas actuales de los mejores precios de compra y venta mediante REST.
- Actualizaciones en vivo de precios de compra y venta de nivel 1 por WebSocket cuando se configura un token de streaming independiente.
- Velas históricas por REST de 1, 5, 10, 15 y 30 minutos; 1, 2, 4, 6, 8 y 12 horas; 1 día; y 1 semana.
- Intervalo de solicitudes y cantidad máxima de instrumentos configurables para controlar el uso de REST.
- No se admiten eventos históricos de nivel 1 ni actualizaciones de velas en vivo.
- Sin operaciones por tick, libros de órdenes, envío de órdenes, datos de cartera ni operaciones de cuenta.

## Uso habitual

Use este conector para listas de seguimiento de Forex, supervisión de cotizaciones, gráficos, investigación y backtesting basado en el historial de velas de Finage.

Se requiere una clave de la API REST de Finage y, para cotizaciones en vivo, un token de streaming adicional. La cobertura de símbolos, la profundidad histórica, el acceso en tiempo real y los límites de solicitudes dependen del plan de Finage contratado.
