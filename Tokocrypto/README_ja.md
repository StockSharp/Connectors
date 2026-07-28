# Tokocrypto コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**Tokocrypto コネクター**は、StockSharp を Tokocrypto の MAIN 現物市場へ接続します。インドネシア向けの暗号資産取引や、Tokocrypto の市場データを StockSharp のメッセージモデルで扱うアプリケーションに適しています。

## 主な機能

- 価格刻み、数量刻み、最小注文条件を含む MAIN 現物銘柄の取得。
- Level 1、Level 2 板、公開約定、OHLCV ローソク足。
- WebSocket によるティッカー、部分板、約定、ローソク足のリアルタイム配信。
- 公開 REST API による過去ローソク足と最新市場スナップショット。
- 現物残高、未約定・過去注文、自己約定履歴。
- 成行、指値、ストップ成行、ストップ指値、post-only、IOC、FOK 注文。
- 個別・一括取消と、設定可能な口座 REST、市場データ REST、WebSocket アドレス。

## 利用例

Tokocrypto の現物銘柄を扱う売買ロボット、ターミナル、市場データ収集、監視サービス、注文管理システムで利用できます。

公開市場データに認証情報は不要です。口座照会と取引には Tokocrypto の API キーとシークレットが必要です。
