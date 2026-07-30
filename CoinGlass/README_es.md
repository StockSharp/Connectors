# Conector de CoinGlass
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de CoinGlass** integra StockSharp con la API de análisis del mercado de criptomonedas de CoinGlass. Convierte los conjuntos seleccionados de futuros, contado, opciones, ETF de Bitcoin y ETF de Ethereum en instrumentos, mensajes de Level 1 y velas históricas de StockSharp.

## Funciones principales

- Seleccionar un tipo de mercado CoinGlass y limitar opcionalmente las consultas por bolsa o símbolo.
- Descubrir los instrumentos disponibles en el conjunto configurado.
- Solicitar indicadores actuales de Level 1, como precio, volumen, variación e interés abierto cuando estén disponibles.
- Consultar instantáneas de Level 1 con un intervalo configurable.
- Descargar series históricas por intervalo de precio, interés abierto, tasa de financiación o liquidaciones.
- Configurar un límite de hasta 1.000 registros históricos por solicitud.

## Uso habitual

Use este conector para paneles de investigación, vigilancia de derivados y análisis histórico de métricas CoinGlass. Configure el token de API, el tipo de mercado y la métrica, y restrinja la bolsa o el símbolo cuando necesite un conjunto específico.

CoinGlass es una fuente analítica, no un centro de ejecución. El adaptador no ofrece órdenes, carteras, operaciones tick a tick ni profundidad de mercado. No admite eventos históricos de Level 1 ni actualización en directo de velas; las solicitudes de velas solo devuelven historia. La disponibilidad y los límites dependen del plan de CoinGlass.
