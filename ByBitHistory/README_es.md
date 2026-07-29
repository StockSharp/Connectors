# Conector de datos históricos de Bybit

El **conector de datos históricos de Bybit** importa en StockSharp los archivos públicos de datos de mercado de Bybit. Normaliza los datos descargables de instrumentos al contado y derivados en el modelo estándar de mensajes de StockSharp.

## Funciones principales

- Búsqueda de instrumentos de mercados al contado, lineales, inversos y de opciones.
- Operaciones históricas tick a tick para los instrumentos al contado y derivados admitidos.
- Datos históricos incrementales del libro de órdenes para los mercados y profundidades admitidos.
- Descargas por rango de fechas para cargas masivas y conjuntos de investigación reproducibles.
- Este adaptador está destinado a datos históricos y no ofrece suscripciones en tiempo real ni enrutamiento de órdenes.
- Los formatos de archivo y los identificadores de mercado de Bybit quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para crear historiales de operaciones y libros de órdenes destinados a análisis, reproducción de mercado y pruebas retrospectivas de estrategias.

Los instrumentos, fechas, profundidades de libro y archivos disponibles dependen de los conjuntos de datos públicos conservados por Bybit.
