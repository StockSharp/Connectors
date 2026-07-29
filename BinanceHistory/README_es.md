# Conector de datos históricos de Binance

El **conector de datos históricos de Binance** importa en StockSharp los archivos públicos de datos de mercado de Binance. Convierte los archivos de la bolsa y los datos de referencia al modelo unificado de mensajes de StockSharp para almacenarlos, analizarlos y reproducirlos de forma coherente.

## Funciones principales

- Cobertura de mercados al contado y de derivados de activos digitales.
- Búsqueda de instrumentos y datos de referencia de contratos.
- Cotizaciones históricas de nivel 1, operaciones tick a tick, libros de órdenes y velas por intervalo.
- Descargas por rango de fechas para completar automáticamente el almacenamiento de datos de mercado.
- Este adaptador está destinado a datos históricos y no ofrece suscripciones en tiempo real ni enrutamiento de órdenes.
- Los formatos de archivo y los identificadores de Binance se normalizan tras la API estándar de StockSharp.

## Uso habitual

Úselo para llenar el almacenamiento local, reparar huecos en las series históricas y preparar datos para investigación y pruebas retrospectivas de estrategias.

Los instrumentos, archivos, rangos de fechas y niveles de detalle disponibles dependen de los archivos publicados por Binance.
