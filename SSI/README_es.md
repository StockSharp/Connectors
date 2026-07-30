# Conector de SSI
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de SSI** conecta StockSharp con SSI FastConnect API v3 para el mercado de valores vietnamita. Traduce los datos de mercado y las operaciones de intermediación de SSI al modelo unificado de mensajes de StockSharp.

## Funciones principales

- Búsqueda de valores e índices de HOSE, HNX y UPCOM, incluidas acciones y los futuros admitidos.
- Cotizaciones de nivel 1, operaciones tick a tick y libros de órdenes en tiempo real, con instantáneas REST iniciales cuando están disponibles.
- Velas históricas por intervalo y posteriores actualizaciones de streaming para los intervalos admitidos.
- Envío, sustitución y cancelación de órdenes individuales, incluidas las condiciones específicas de SSI.
- Búsqueda de cuentas y actualizaciones de saldos, posiciones, órdenes y ejecuciones mediante streaming y conciliación periódica.
- Endpoints REST y WebSocket e intervalo de consulta de cartera configurables.
- Se requieren credenciales de FastConnect; la negociación también depende del Client ID, la cuenta, la clave privada RSA y el OTP vigente.
- Las sesiones, los formatos y los temas de streaming de SSI quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para terminales del mercado vietnamita, estrategias en vivo, servicios de gestión de órdenes y herramientas de supervisión con acceso directo al bróker SSI.

Los instrumentos, la profundidad histórica, los permisos de negociación, los límites y la disponibilidad dependen de SSI y de los derechos de la cuenta FastConnect conectada.
