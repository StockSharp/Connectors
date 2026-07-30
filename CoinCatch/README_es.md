# Conector de CoinCatch
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de CoinCatch** integra StockSharp con los mercados al contado y de derivados de CoinCatch. La configuración de producto permite elegir contado, futuros con margen en USDT o futuros con margen en moneda, y las API REST y WebSocket proporcionan datos y negociación autenticada.

## Funciones principales

- Descubrir instrumentos del producto CoinCatch seleccionado.
- Suscribirse a Level 1, profundidad de mercado, operaciones tick a tick y velas por intervalo.
- Descargar velas históricas y continuar con actualizaciones en directo por WebSocket.
- Enviar órdenes limitadas y de mercado, con reduce-only para futuros y post-only para órdenes limitadas.
- Cancelar una orden o todas las órdenes de un símbolo.
- Cargar saldos, posiciones, órdenes abiertas e históricas y operaciones propias.
- Conciliar el estado privado mediante clave, secreto y frase de contraseña de API.

## Uso habitual

Use este conector para vigilar mercados al contado o de futuros, obtener históricos de velas y automatizar operaciones en CoinCatch. Seleccione el producto antes de conectar y facilite credenciales con permisos de lectura o negociación adecuados para las funciones privadas.

El adaptador no expone las órdenes planificadas o de activación de CoinCatch, órdenes iceberg ni sustitución atómica. El libro se entrega mediante instantáneas y no hay flujo de registro de órdenes. Deben respetarse las reglas de instrumento, el modo de cuenta, los permisos y los límites de la bolsa.
