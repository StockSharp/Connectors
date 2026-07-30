# Coinalyze-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Coinalyze-Konnektor** verbindet StockSharp mit der Coinalyze-API für Kryptomarktanalysen. Historische Preise und Derivatekennzahlen werden als standardisierte StockSharp-Zeitrahmenkerzen für Futures- oder Spot-Instrumente abgebildet.

## Wichtige Funktionen

- Futures- oder Spot-Instrumente wählen und die Suche optional auf eine Börse einschränken.
- Historische Kerzen für Preis, Open Interest, Funding Rate, Liquidationen oder Long-/Short-Verhältnis laden.
- Die von der Coinalyze-API unterstützten Zeitrahmen nutzen.
- Open-Interest- und Liquidationswerte optional in US-Dollar umrechnen.
- Ein Verlaufslimit von bis zu 2.000 Datensätzen pro Anfrage festlegen.
- Anfragen mit einem Coinalyze-API-Token authentifizieren.

## Typische Verwendung

Der Konnektor eignet sich für Backtests, Derivate-Research und vergleichende Analysen historischer Coinalyze-Kennzahlen. Vor dem Abonnement werden Markttyp und Kerzenmetrik gewählt; ein Börsenfilter kann das Untersuchungsuniversum begrenzen.

Der Adapter arbeitet ausschließlich historisch über REST. Live-Kerzen, Level 1, Tick-Trades, Markttiefe, Portfolios und Orderausführung werden nicht bereitgestellt. Verfügbare Symbole, Intervalle, Verlaufstiefe und Anfrageraten bestimmt die Coinalyze-API.
