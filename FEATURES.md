# Состояние функций

13.10.2026. «Реализовано в коде» и «проверено без игры» не означают готовность функции в Transport Fever 3. Установленной игры, Steam-сессии и настоящих saves нет. Стабильный выпуск не подготовлен.

| Область | Состояние кода | Подтверждение и предел |
|---|---|---|
| Хост одновременно играет/служит сервером | Существующая архитектура MPFever | Relay/Client и сборка испытаны; игра/WAN/NAT не испытаны |
| Безопасный декодер | Исправлено | Реальные Lua/C# parser tests, limits; Lua expressions не исполняются |
| TCP/IPC и медленный peer | Ограничены | Настоящие sockets, fragmented/oversized/timeout, independent queues; memory budget игры не принят |
| Граница принятия TCP-клиента | Broadcast/адресная отправка только Joined; connection первым; 4 КиБ handshake | Real TCP regressions в PR #6; initial identity/TLS ещё отсутствуют |
| Снимок мира | Потоковый hash/atomic commit/cleanup | Сгенерированный файл 32 MiB, 1/4/8 peers; настоящий save/load не испытан |
| Переподключение | Credential, accepted handshake, full-server replacement | Real TCP; session token + permanent key, без Steam proof/host migration |
| Реестр отдельных компаний | Upstream + исправленный sid/start signature | Pure Lua match/join/serialize/collision tests; PLAYER entities в игре не проверялись |
| Право указать co | Host roster/proven persistent owner и schema | C# negative/restart tests; реальные roster timing/save/load требуют engine gate |
| Флот и линии | Дополнительная проверка resolved owner | Lua policy с injected ownership; native graph/terrain/assets и разрешённые refs неполны |
| Бюджет и кредиты | Частично upstream; Repay берёт сохранённые terms | Real coLoanEvent с mock dependencies; Obtain/месячная схема/формула game loan_util не проверены |
| Прогресс/разрешения | Существующий общий progression fallback | Независимый прогресс ещё не реализован/не подтверждён |
| Контракты | Upstream owner/filter wrappers | Настоящие offers/rewards/delivery не проверены |
| Конкуренция грузов/пассажиров | Используются штатные economy/pathfinding | Поведение разных компаний не измерено; новой искусственной экономики нет |
| Платная инфраструктура | Upstream shared station/per-stop fee | Stable visit ID, reroute/reload/double billing не решены; пути/депо по договорам отсутствуют |
| Центральный порядок и epochs | Offline journal/cursor/quorum реализованы | Классические file/replay tests; **не подключены к игровому scheduler** |
| Владелец после перезапуска | Owner fingerprint сохраняется с company id; RSA key в LocalAppData, explicit legacy assignment | Python registry roundtrip + реальные C# файлы/TCP/net48; настоящий save/restart пока NOT RUN |
| Полная Windows сборка | Обязательная CI job | net48 EXE, x64 DLL, 14 exports/ordinals, ZIP/license; hooks не запускались |
| Локализация | 33 gaps заполнены | 227 используемых catalog keys в 11 локалях, placeholders проверяются; диагностика новых компонентов может быть на английском |
| Gigantomaniac | Цель будущего этапа | Ни карты, ни saves/RAM/game step profiling нет |

История принятых исправлений: [#2](https://github.com/WestDed/TransportFever3-Multiplayer/pull/2) → [#3](https://github.com/WestDed/TransportFever3-Multiplayer/pull/3) → [#4](https://github.com/WestDed/TransportFever3-Multiplayer/pull/4) → [#5](https://github.com/WestDed/TransportFever3-Multiplayer/pull/5) → [#6](https://github.com/WestDed/TransportFever3-Multiplayer/pull/6). Документы продолжения находятся в docs/STAGE1_*.md. Исторический [аудит функций](https://github.com/WestDed/TransportFever3-Multiplayer/blob/6611e8f63e29e04aeb978bddd17877b461894edf/FEATURES.md) остаётся доступен.

PR #8 слит заказчиком в main 5f6cd58. [PR #9](https://github.com/WestDed/TransportFever3-Multiplayer/pull/9) исправляет ambiguity pending owner, частичный roster commit/stale authority, sid refresh и invalid host/world assignment. [Разбор и результаты](docs/MAIN_REVIEW_2026_10_13.md); фактическое слияние PR #9 проверять через GitHub.
