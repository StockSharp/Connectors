# Conector de WazirX
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de WazirX** conecta StockSharp con el exchange centralizado de criptomonedas al contado WazirX. Traduce los datos REST y WebSocket y las operaciones de cuenta al modelo unificado de mensajes de StockSharp.

## Funciones principales

- Descubrimiento de mercados al contado con incrementos de precio y cantidad y reglas de negociación.
- Nivel 1, operaciones tick a tick, libros y velas en tiempo real mediante flujos públicos.
- Instantáneas REST e historial disponible de operaciones y velas antes de continuar en vivo.
- Órdenes limitadas y stop-limit admitidas, cancelación individual o grupal filtrada y actualizaciones de órdenes y ejecuciones.
- Saldos y carteras mediante flujos privados con conciliación REST.
- Las operaciones privadas requieren clave y secreto API; los datos públicos no necesitan credenciales de negociación.
- El adaptador no ofrece órdenes de mercado ni sustitución atómica.
- La autenticación, los símbolos, transportes, filtros y formatos de WazirX quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para terminales al contado de WazirX, estrategias en vivo, gráficos, supervisión de cuentas y gestión de órdenes.

Los mercados, el historial, los stop-limit, los permisos, los filtros, los límites y la disponibilidad dependen de WazirX y de la cuenta conectada.
