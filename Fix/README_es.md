# Conector del protocolo FIX

El **conector del protocolo FIX** conecta StockSharp con brókeres, bolsas y sistemas de negociación mediante sesiones configurables de Financial Information eXchange. Asigna los mensajes específicos de cada dialecto al modelo unificado de mensajes de StockSharp.

## Funciones principales

- Dialectos FIX configurables para distintos brókeres, mercados y segmentos.
- Inicio de sesión, autenticación, latidos, control de secuencias, reenvíos, reconexión y transporte seguro opcional.
- Búsqueda de instrumentos y datos como nivel 1, operaciones, libros, velas, noticias y eventos del registro de órdenes cuando el dialecto los admite.
- Envío, modificación y cancelación de órdenes, cancelación masiva, consulta de estado y procesamiento de ejecuciones cuando la contraparte los admite.
- Actualizaciones de carteras, saldos y posiciones para sesiones transaccionales.
- Configuración de remitente, destino, cuenta, extremo y sesión mediante el modelo estándar de StockSharp.

## Uso habitual

Úselo para integraciones personalizadas con brókeres, pasarelas bursátiles, estrategias en vivo, servicios de gestión de órdenes y acceso normalizado a datos de mercado.

Los mensajes, campos, tipos de orden, recuperación y permisos dependen del dialecto FIX elegido y de la especificación de sesión de la contraparte.
