# Состояние функций

15.10.2026. Есть первый пользовательский PASS совместного подключения/игры через Porthole. Большая карта, доступ к инфраструктуре, субсидии и чужие уведомления выявили отказы/недостающие функции. В среде разработчика игры нет; автоматические проверки и пользовательские испытания различаются. Стабильный выпуск не подготовлен. [Матрица всех замечаний и PR #11](docs/COOP_PLAYTEST_2026_10_15.md).

| Область | Состояние кода | Подтверждение и предел |
|---|---|---|
| Хост одновременно играет/служит сервером | Существующая архитектура MPFever | Пользователи сообщили об игре на двух ПК через Porthole; длительность/другие сети не приняты |
| Безопасный декодер | Исправлено | Реальные Lua/C# parser tests, limits; Lua expressions не исполняются |
| TCP/IPC и медленный peer | Ограничены | Настоящие sockets, fragmented/oversized/timeout, independent queues; memory budget игры не принят |
| Граница принятия TCP-клиента | Broadcast/адресная отправка только Joined; connection первым; 4 КиБ handshake | Real TCP regressions в PR #6; initial identity/TLS ещё отсутствуют |
| Снимок мира | Потоковый hash/atomic commit/cleanup | 32 MiB TCP стенд; первый пользовательский join прошёл, проблемный большой save дал reconnect; повтор PR #11 нужен |
| Переподключение | Credential, accepted handshake, full-server replacement | Real TCP; session token + permanent key, без Steam proof/host migration |
| Реестр отдельных компаний | Upstream + исправленный sid/start signature | Pure Lua regressions; пользователи играли отдельными компаниями, actual save/restart/ownership ещё не принят |
| Право указать co | Host roster/proven persistent owner и schema | C# negative/restart tests; реальные roster timing/save/load требуют engine gate |
| Флот и линии | Дополнительная проверка resolved owner | Lua policy с injected ownership; native graph/terrain/assets и разрешённые refs неполны |
| Бюджет и кредиты | Частично upstream; Repay берёт сохранённые terms | Real coLoanEvent с mock dependencies; Obtain/месячная схема/формула game loan_util не проверены |
| Прогресс/разрешения | Существующий общий progression fallback | Независимый прогресс ещё не реализован/не подтверждён |
| Контракты | Exclusive city claim + собственные delivery orders/escrow, см. таблицу ниже | Core/adapters PASS; настоящие offers/rewards/delivery NOT RETESTED |
| Конкуренция грузов/пассажиров | Используются штатные economy/pathfinding | Поведение разных компаний не измерено; новой искусственной экономики нет |
| Платная инфраструктура | Road grants/tolls/visits и station modes/billing, см. таблицу ниже | Core/adapters PASS; реальные pathfinding/fees NOT RETESTED, пути/депо отдельно |
| Центральный порядок и epochs | Offline journal/cursor/quorum реализованы | Классические file/replay tests; **не подключены к игровому scheduler** |
| Владелец после перезапуска | Owner fingerprint сохраняется с company id; RSA key в LocalAppData, explicit legacy assignment | Python registry roundtrip + реальные C# файлы/TCP/net48; настоящий save/restart пока NOT RUN |
| Полная Windows сборка | Обязательная CI job | net48 EXE, x64 DLL, 14 exports/ordinals, ZIP/license; hooks не запускались |
| Игровой каталог ресурсов | Исправлен в PR #10: permissions module зарегистрирован; harness учитывает catalog | Реальный ZIP/copy hashes и Lua bootstrap fixture PASS; первый запуск FAIL; после catalog fix startup/join USER-REPORTED PASS |
| Локализация | 33 gaps заполнены | 279 используемых catalog keys в 11 локалях, placeholders проверяются; диагностика новых компонентов может быть на английском |
| Gigantomaniac | Цель будущего этапа | Ни карты, ни saves/RAM/game step profiling нет |

История принятых исправлений: [#2](https://github.com/WestDed/TransportFever3-Multiplayer/pull/2) → [#3](https://github.com/WestDed/TransportFever3-Multiplayer/pull/3) → [#4](https://github.com/WestDed/TransportFever3-Multiplayer/pull/4) → [#5](https://github.com/WestDed/TransportFever3-Multiplayer/pull/5) → [#6](https://github.com/WestDed/TransportFever3-Multiplayer/pull/6). Документы продолжения находятся в docs/STAGE1_*.md. Исторический [аудит функций](https://github.com/WestDed/TransportFever3-Multiplayer/blob/6611e8f63e29e04aeb978bddd17877b461894edf/FEATURES.md) остаётся доступен.

PR #8 слит заказчиком в main 5f6cd58. [PR #9](https://github.com/WestDed/TransportFever3-Multiplayer/pull/9) исправляет ambiguity pending owner, частичный roster commit/stale authority, sid refresh и invalid host/world assignment. [Разбор и результаты](docs/MAIN_REVIEW_2026_10_13.md); фактическое слияние PR #9 проверять через GitHub.

## Полный список первого совместного испытания — PR #11 / 0.3.7-experimental

| Функция | Что реализовано | Предел проверки |
|---|---|---|
| Большой save, 50% в игровом loading screen | Control reserve, verified receipt, раздельные deadlines, launcher liveness, snapshot-specific ready; healthy socket остаётся подключён | Queue/TCP/deadline regressions; root cause engine stall без свежих logs не установлен |
| Строительство вдоль чужой дороги | Окно владельца, общие/адресные use/connect/edit права, проверки подключения/split/сноса/road stop, сохранённый logical owner | Core/engine/GUI fixtures; native pre-apply enforcement и вся матрица реальных builders NOT RETESTED |
| Платные дороги | Private/free/toll, одна плата за вход на участок, debit→credit, сохранённый visit, pause при отсутствии денег, содержание за владельцем | Regression tests, journal callback fixtures; настоящий traffic/pathfinding и save frontier не проверены |
| Чужие остановки | Private/free/toll и адресное use; общий owner resolver, live selection/reload, line validation и остановка существующего транспорта при revoke; saved billing queue | UI/core/engine fixtures; реальные stop/line/arrival/payment NOT RETESTED |
| Одна городская субсидия — один владелец | Existing/unclaimed/ready guard, closed/retry/ambiguous UID refusal, saved claim | Regressions; simultaneous native apply и actual rewards требуют игры |
| Собственные заказы | Окно создания, реальные cargo/passenger ids и target, escrow, один carrier, реальные delivery events, cancel/abandon/expiry/refund | Pure core и production adapters с API fixtures; реальные события TF3 NOT RETESTED |
| Чужие уведомления и звуки | GUI state/history/map markers/popup prefilter по владельцу company/entity; свои и мировые остаются | Production projection tests; реальные notification recipes/sounds NOT RETESTED |
| Чужие денежные надписи | Native filter отдельного GUI destination после replication, с alias guard; быстрее reassert company context | Production C++ helper исполняется Linux/Windows CI; engine hooks и полный ACCOUNT/JOURNAL итог NOT RETESTED |

[Правила и инструкция](docs/BUSINESS_MECHANICS.md). Весь пользовательский список реализован в коде; эти записи не означают принятую игровую работоспособность. Пограничная проводка submitted при save становится uncertain и не повторяется автоматически. Durable central cutover, TLS, host migration и Gigantomaniac остаются отдельными этапами проекта.
