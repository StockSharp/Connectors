# Conector de datos históricos de Gate.io

El **conector de datos históricos de Gate.io** importa en StockSharp los archivos públicos de datos de mercado de Gate.io. Convierte los conjuntos de datos al contado y de derivados al modelo unificado de mensajes de StockSharp para su almacenamiento, análisis y reproducción.

## Funciones principales

- Búsqueda de instrumentos de mercados al contado, futuros perpetuos y futuros con entrega.
- Operaciones históricas tick a tick, libros de órdenes incrementales y velas por intervalo.
- Descargas por rango de fechas para completar sistemáticamente los datos de mercado.
- Los símbolos nativos y las variantes de mercado se asignan a identificadores de instrumentos de StockSharp.
- Este adaptador está destinado a datos históricos y no ofrece suscripciones en tiempo real ni enrutamiento de órdenes.
- Los formatos de archivo de Gate.io quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para preparar historiales de criptoactivos destinados a gráficos, análisis, investigación de libros de órdenes y pruebas retrospectivas de estrategias.

Los instrumentos, archivos, fechas, profundidades e intervalos de velas disponibles dependen de los conjuntos de datos públicos publicados por Gate.io.
