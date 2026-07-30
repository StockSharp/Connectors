# Conector de J-Quants
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de J-Quants** conecta StockSharp con J-Quants API V2 para obtener datos históricos y de referencia del mercado japonés. Es un adaptador REST de solo lectura destinado a la investigación, no al streaming de mercado en vivo ni a la negociación.

## Funciones principales

- Descubrimiento y datos de referencia de acciones, futuros y opciones japoneses, incluidos subyacentes, precios de ejercicio, tipos de opción y vencimientos de derivados.
- Un mensaje de nivel 1 generado a partir de la última barra diaria disponible; no es una suscripción de cotizaciones en vivo.
- Operaciones históricas por tick para acciones; el historial de ticks no está disponible para futuros ni opciones.
- Velas históricas de acciones de 1, 5, 15 y 30 minutos; 1 hora; y 1 día.
- Velas históricas diarias de futuros y opciones.
- Retraso configurable entre llamadas REST y profundidad máxima de paginación.
- Sin libros de órdenes, actualizaciones en vivo, envío de órdenes, datos de cartera ni operaciones de cuenta.

## Uso habitual

Use este conector para catálogos de instrumentos japoneses, investigación histórica, gráficos, preparación de datos y backtesting con conjuntos de datos de J-Quants.

Se requiere una clave de J-Quants API V2. Los endpoints, instrumentos, rangos de fechas, paginación y frecuencias de solicitudes disponibles dependen del plan contratado; los valores de nivel 1 proceden de una barra diaria y no deben interpretarse como precios de compra y venta en tiempo real.
