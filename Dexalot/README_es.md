# Conector de Dexalot
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Dexalot** conecta StockSharp con el libro central de órdenes limitadas en cadena de Dexalot, alojado en Dexalot L1 sobre Avalanche. Combina datos públicos por REST y WebSocket con llamadas a contratos EVM para la negociación al contado y el estado de la cuenta.

## Funciones principales

- Descubrimiento y datos de referencia de los pares de tokens al contado de Dexalot.
- Instantáneas de nivel 1 y del libro mediante lecturas de contratos, seguidas de actualizaciones en vivo por WebSocket; no hay eventos históricos de nivel 1 ni del libro.
- Flujos de operaciones y velas por WebSocket, con filtros de fecha y cantidad sobre el historial entregado por el proveedor y recepción continua en vivo.
- Velas de 5, 15 y 30 minutos; 1 y 4 horas; y 1 día.
- Órdenes limitadas y de mercado en cadena, con comportamiento post-only y prevención de autonegociación configurable.
- Modificación, cancelación individual y cancelación masiva de órdenes; no se admiten órdenes iceberg, vencimiento absoluto ni cierre masivo de posiciones.
- Saldos de tokens de la cartera, historial de órdenes y ejecuciones, y conciliación del estado privado mediante REST, WebSocket y RPC de EVM.

## Uso habitual

Use este conector para estrategias al contado en vivo, terminales y servicios de gestión de órdenes que necesiten el libro de Dexalot y ejecución en cadena.

Para negociar se requieren una dirección de monedero y una clave privada, y se incurre en gas y latencia de confirmación de la red. Los pares disponibles, el historial suministrado por el flujo, los límites de API, la disponibilidad de contratos y la finalidad dependen de Dexalot y de los endpoints de red elegidos.
