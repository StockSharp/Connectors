# Conector de Balancer
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Balancer** conecta StockSharp con un protocolo de negociación y liquidez en cadena. Traduce los datos y las operaciones específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo en distintos mercados.

## Funciones principales

- Cobertura habitual: activos en cadena y pools de liquidez.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Datos de mercado admitidos por el adaptador: cotizaciones de nivel 1, operaciones tick a tick y velas.
- Solicitudes de datos históricos para gráficos, análisis y pruebas retrospectivas.
- Envío de swaps o transacciones de blockchain admitidos por el proveedor.
- Actualizaciones de carteras, saldos, posiciones y estado de ejecuciones.
- Suscripciones en tiempo real mediante el transporte de streaming del proveedor.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para estrategias en vivo, terminales, servicios de gestión de órdenes y herramientas de supervisión que necesiten acceso directo al proveedor.

Las redes, pools, instrumentos y funciones de transacción disponibles dependen de Balancer, de los servicios RPC o de indexación y de los permisos de la cartera.
