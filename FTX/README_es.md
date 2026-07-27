# Conector de FTX
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de FTX** conecta StockSharp con una integración heredada con un exchange de activos digitales. Traduce los datos y las operaciones específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo en distintos mercados.

Es posible que el servicio original ya no esté disponible. La integración se conserva por compatibilidad, para mantener sistemas existentes y para estudiar una implementación completa de un conector.

## Funciones principales

- Cobertura habitual: activos digitales, mercados al contado, derivados.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Datos de mercado admitidos por el adaptador: cotizaciones de nivel 1, operaciones tick a tick, libros de órdenes y velas.
- Solicitudes de datos históricos para gráficos, análisis y pruebas retrospectivas.
- Flujos de envío de órdenes y ejecuciones admitidos por el proveedor.
- Actualizaciones de carteras, saldos, posiciones y estado de ejecuciones.
- Suscripciones en tiempo real mediante el transporte de streaming del proveedor.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para mantener una integración existente o como código fuente práctico para aprender a mapear datos, transacciones y detalles de protocolo a StockSharp.

Antes de usarlo en producción, compruebe que la API original y las direcciones necesarias sigan disponibles.
