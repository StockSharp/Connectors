# Conector CoinTR

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector CoinTR** conecta StockSharp con CoinTR, una bolsa de criptomonedas orientada al mercado turco. Proporciona acceso a los instrumentos al contado de CoinTR mediante el modelo estándar de mensajes de StockSharp.

## Funciones principales

- Descubrimiento de instrumentos al contado con precisión de precio y cantidad y límites de negociación.
- Cotizaciones de nivel 1, instantáneas del libro de órdenes de nivel 2 y operaciones públicas.
- Tickers, libros, operaciones y velas en tiempo real mediante WebSocket.
- Velas OHLCV históricas para los intervalos admitidos por CoinTR.
- Saldos de cartera, órdenes activas y notificaciones privadas de ejecuciones.
- Envío de órdenes de mercado, limitadas y activadas, y cancelación de órdenes.
- Direcciones REST y WebSocket pública y privada configurables.

## Uso habitual

Use el conector en robots de trading, terminales, recopiladores de datos, sistemas de supervisión y servicios de gestión de órdenes que operen en los mercados al contado de CoinTR.

Los datos públicos no requieren credenciales. Las operaciones y el acceso a la cuenta requieren una clave API, un secreto y una frase de contraseña con los permisos adecuados. En una compra a mercado, CoinTR interpreta el volumen como un importe en la moneda cotizada; las órdenes limitadas y las ventas a mercado usan la cantidad del activo base.
