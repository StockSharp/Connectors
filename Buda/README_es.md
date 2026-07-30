# Conector de Buda
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Buda** integra StockSharp con el mercado al contado de criptomonedas de Buda.com. Los datos públicos están disponibles sin credenciales, mientras que las operaciones REST autenticadas utilizan una clave y un secreto de API.

## Funciones principales

- Descubrir los instrumentos al contado ofrecidos por Buda.
- Suscribirse a cotizaciones de Level 1, profundidad de mercado y operaciones tick a tick.
- Combinar actualizaciones públicas por WebSocket con instantáneas y conciliación por REST.
- Enviar órdenes limitadas y de mercado, y cancelar órdenes individuales o en grupo.
- Cargar saldos, estado de cartera, órdenes activas e históricas y operaciones propias.
- Conciliar el estado privado con un intervalo de consulta configurable.

## Uso habitual

Use este conector para vigilar en tiempo real el mercado al contado de Buda y operar de forma autenticada desde StockSharp. Las aplicaciones de datos públicos no necesitan credenciales; las órdenes y los datos de cuenta requieren una clave y un secreto de Buda con los permisos necesarios.

El adaptador no proporciona velas ni un flujo de registro de órdenes, y entrega el libro de órdenes mediante instantáneas, no incrementos. No admite la sustitución atómica de órdenes, por lo que la estrategia debe cancelar la orden anterior y enviar otra por separado. Se aplican los permisos y límites de la bolsa.
