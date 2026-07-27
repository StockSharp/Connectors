# Conector de CSV
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de CSV** conecta StockSharp con una fuente de datos configurable. Traduce los datos y las operaciones específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo en distintos mercados.

## Funciones principales

- Cobertura habitual: acciones, futuros, opciones.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Datos de mercado admitidos por el adaptador: cotizaciones de nivel 1, operaciones tick a tick, libros de órdenes y velas.
- Solicitudes de datos históricos para gráficos, análisis y pruebas retrospectivas.
- Actualizaciones de carteras, saldos, posiciones y estado de ejecuciones.
- Este adaptador está destinado a datos de mercado y no enruta órdenes.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para alimentar gráficos, almacenamiento de mercado, análisis, investigación y pruebas de estrategias con datos del proveedor.

Los campos, instrumentos y períodos disponibles dependen de la fuente de datos configurada.
