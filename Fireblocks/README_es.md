# Conector de Fireblocks
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Fireblocks** conecta StockSharp con un servicio institucional, de custodia o liquidación de activos digitales. Traduce los datos y las operaciones específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo en distintos mercados.

## Funciones principales

- Cobertura habitual: activos digitales, Forex y CFD.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Solicitudes de datos históricos para gráficos, análisis y pruebas retrospectivas.
- Flujos de cuentas, transferencias y transacciones admitidos por el proveedor.
- Actualizaciones de carteras, saldos, posiciones y estado de ejecuciones.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para estrategias en vivo, terminales, servicios de gestión de órdenes y herramientas de supervisión que necesiten acceso directo al proveedor.

Los instrumentos, la profundidad de datos, los permisos de negociación, los límites y la disponibilidad dependen de Fireblocks, del plan de API y de la cuenta conectada.
