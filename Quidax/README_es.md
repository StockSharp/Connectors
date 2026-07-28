# Conector Quidax

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector Quidax** integra StockSharp con el mercado al contado de Quidax. Resulta especialmente útil para observar y negociar criptomonedas cotizadas en NGN y otras monedas fiduciarias africanas, además de pares entre criptomonedas.

## Funciones principales

- Descubrimiento de instrumentos al contado con composición del par, precisión de precio y volumen y valor mínimo de orden.
- Cotizaciones de nivel 1, libros de órdenes de nivel 2, operaciones públicas y velas históricas.
- Suscripciones continuas de datos mediante consultas REST con intervalo configurable.
- Saldos de carteras, órdenes abiertas e históricas y ejecuciones privadas.
- Órdenes limitadas y de mercado, cancelación individual y cancelación masiva filtrada.
- Dirección REST, identificador de cuenta o subcuenta e intervalo de consulta configurables.

Los datos públicos están disponibles sin credenciales. Las operaciones de cartera y negociación requieren una clave secreta de Quidax. El identificador `me` apunta al propietario del token y puede sustituirse por un identificador de subcuenta compatible.
