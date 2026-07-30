# Conector de CoinSwitch
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de CoinSwitch** integra StockSharp con las API de CoinSwitch PRO. La configuración de producto permite elegir mercados al contado en INR o USDT, futuros perpetuos con margen en USDT o la interfaz HFT de opciones en beta privada.

## Funciones principales

- Descubrir instrumentos del producto CoinSwitch seleccionado.
- Suscribirse a Level 1, profundidad, operaciones tick a tick y velas por intervalo.
- Descargar el historial de velas y recibir actualizaciones por WebSocket cuando el producto y el intervalo lo permitan.
- Enviar órdenes limitadas al contado; limitadas, de mercado o stop-market en futuros; y limitadas o de mercado en opciones HFT.
- Usar reduce-only en órdenes de derivados compatibles y modos de vigencia admitidos para opciones HFT.
- Cancelar una orden o grupos de órdenes coincidentes.
- Cargar saldos, posiciones, órdenes abiertas e históricas y operaciones propias.

## Uso habitual

Use este conector para vigilar CoinSwitch PRO y automatizar operaciones en una superficie de producto seleccionada. Las funciones privadas requieren clave de API y secreto Ed25519 con permisos adecuados; las opciones también exigen acceso a la beta privada HFT de CoinSwitch.

Las funciones varían: contado solo admite órdenes limitadas, la entrada condicional solo se implementa en futuros como stop-market y las velas de opciones no usan WebSocket. No hay sustitución atómica, órdenes iceberg o GTD, libros incrementales ni flujo de registro de órdenes. Se aplican permisos y límites de CoinSwitch.
