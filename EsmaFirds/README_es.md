# Conector de ESMA FIRDS
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de ESMA FIRDS** conecta StockSharp con un servicio de datos financieros e información de referencia. Traduce los datos específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo con distintas fuentes de datos.

## Funciones principales

- Cobertura habitual: acciones y datos de referencia de emisores.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Datos de mercado, empresas, presentaciones, divulgaciones y referencia admitidos por el proveedor.
- Este adaptador está destinado al acceso a datos y no enruta órdenes.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para datos maestros de valores, supervisión de divulgaciones, investigación de emisores, procesos de cumplimiento y análisis histórico.

Los instrumentos, la profundidad de datos, los permisos de negociación, los límites y la disponibilidad dependen de ESMA FIRDS, del plan de API y de la cuenta conectada.
