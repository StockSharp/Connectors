# Conector de Transaq

El **conector de Transaq** conecta StockSharp con un servidor de negociación Transaq utilizado por brókeres rusos. Convierte los comandos XML y las actualizaciones asíncronas del servidor al modelo unificado de mensajes de StockSharp.

## Funciones principales

- Búsqueda de instrumentos, mercados, tableros y tipos de datos admitidos para acciones, futuros y opciones.
- Cotizaciones de nivel 1, operaciones tick a tick, libros incrementales, velas y noticias en tiempo real.
- Solicitudes históricas de ticks y velas admitidas por el servidor.
- Flujos de órdenes estándar, condicionales, stop, repo y negociadas, incluidas modificación y cancelación.
- Actualizaciones de carteras, límites, apalancamiento, efectivo, posiciones, órdenes y operaciones propias.
- Extremos de producción y demostración, proxy, cambio de contraseña, latido y procesamiento secuencial de comandos.

## Uso habitual

Úselo para terminales de bróker, estrategias en vivo del mercado ruso, gestión de órdenes, supervisión de cuentas, gráficos y análisis histórico.

Los instrumentos, el historial, los tipos de orden, los campos de cuenta y los permisos dependen del servidor Transaq, la configuración del bróker y la cuenta conectada.
