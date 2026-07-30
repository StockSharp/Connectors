# Conector de Pendle
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Pendle** conecta StockSharp con un protocolo de negociación de rendimiento en cadena. Traduce los datos del protocolo y las operaciones de la cartera al modelo unificado de mensajes de StockSharp, permitiendo usar suscripciones y flujos de transacción estándar en los mercados de Pendle.

## Funciones principales

- Cobertura habitual: activos con rendimiento en cadena, tokens de principal, tokens de rendimiento y mercados de Pendle.
- Búsqueda de instrumentos y datos de referencia del protocolo.
- Datos de mercado admitidos por el adaptador: cotizaciones de nivel 1 y velas.
- Solicitudes de velas históricas y actualizaciones continuas de mercado para gráficos, análisis y flujos de estrategia.
- Conversión de tokens y envío de transacciones de blockchain admitidos por el proveedor, incluidas las aprobaciones de tokens necesarias.
- Actualizaciones de cartera, saldos, posiciones y estado de ejecuciones de la billetera.
- El transporte HTTP y RPC, las transacciones de cartera y los formatos del protocolo quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para supervisar mercados de rendimiento, ejecutar estrategias en vivo, crear herramientas conscientes de la cartera y obtener cotizaciones o ejecutar conversiones mediante Pendle.

Las redes, mercados, tokens, cotizaciones, funciones de transacción, comisiones y disponibilidad dependen de Pendle, de los puntos finales API y RPC configurados, del estado actual de la cadena y de los permisos de la cartera.
