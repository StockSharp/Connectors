# Conector de Coinmetro
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Coinmetro** integra StockSharp con la bolsa de criptomonedas al contado Coinmetro. Combina endpoints REST para instrumentos, cuenta, órdenes y velas con actualizaciones WebSocket de mercado y actividad privada, y admite entornos independientes real y de demostración.

## Funciones principales

- Descubrir instrumentos al contado de Coinmetro y sus restricciones de negociación.
- Suscribirse por WebSocket a Level 1, profundidad y operaciones tick en directo.
- Descargar velas históricas de 1, 5 y 30 minutos, 4 horas y un día.
- Enviar órdenes limitadas y de mercado con parámetros compatibles GTC, IOC, FOK y GTD.
- Cancelar una orden o grupos de órdenes abiertas coincidentes.
- Cargar saldos, órdenes abiertas e históricas y operaciones propias.
- Alternar entre endpoints REST y WebSocket configurables para real y demostración.

## Uso habitual

Use este conector para vigilar el mercado al contado de Coinmetro, cargar históricos de velas y automatizar operaciones. Configure un token con los permisos necesarios para funciones privadas reales; el modo de demostración usa endpoints abiertos separados y puede obtener automáticamente un token demo.

Las velas son solo históricas y no continúan con actualizaciones en directo. No se admiten sustitución atómica, órdenes condicionales, iceberg ni post-only, y los libros se publican como instantáneas, no como incrementos de StockSharp. Considere el intervalo de conciliación privada y los límites de API al diseñar estrategias.
