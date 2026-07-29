# Conector de datos históricos de KuCoin

El **conector de datos históricos de KuCoin** importa en StockSharp los archivos públicos de datos de mercado de KuCoin. Normaliza los datos descargables de los mercados al contado y de futuros en el modelo unificado de mensajes de StockSharp.

## Funciones principales

- Búsqueda de instrumentos y datos de referencia para mercados al contado y de futuros.
- Operaciones históricas tick a tick, libros de órdenes y velas por intervalo.
- Descargas por rango de fechas para completar de forma reproducible el almacenamiento de datos de mercado.
- Los símbolos de la bolsa y los segmentos de mercado se asignan a identificadores de instrumentos de StockSharp.
- Este adaptador está destinado a datos históricos y no ofrece suscripciones en tiempo real ni enrutamiento de órdenes.
- El transporte y los formatos de archivo de KuCoin quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para preparar historiales de KuCoin destinados a gráficos, análisis, reproducción de mercado y pruebas retrospectivas de estrategias.

Los instrumentos, archivos, fechas, profundidades e intervalos de velas disponibles dependen de los conjuntos de datos públicos conservados por KuCoin.
