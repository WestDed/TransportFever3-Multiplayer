# Состояние функций

19.10, PR #18: [предзапусковое лобби, каталог сохранений и SHA-кеш](docs/LOBBY_AND_SNAPSHOT_CACHE.md) реализованы в .5/protocol 10; игровой повтор NOT RETESTED. Готовность, режим компаний и выбор существующей карты до запуска TF3; точный локальный снимок позволяет пропустить bulk. Исправлено отключение speed=3; нет обещания ускорить изменённый мир или устранить все наблюдаемые loan/native desync.

18.10: [передача snapshot .4](docs/INCIDENT_2026_10_18_SNAPSHOT_QUEUE.md). Успешный join и начало получения 174497012 bytes подтверждены пользовательскими logs; завершение передачи/загрузка клиента не подтверждены. Исправлена воспроизведённая на C# переполненная очередь статусов; фактический повтор через Porthole ещё нужен.

18.10.2026: пользователь подтвердил fatal crash пакета .2 при запуске карты; [разбор и фикс .3](docs/INCIDENT_2026_10_18_MAP_LOAD.md). Устранён межскриптовый доступ через просроченный callback handle. Это исправление по реальному стеку и регрессиям; успешный повтор загрузки TF3 ещё нужен. Остальные mechanics из #11/#12/#14/#15 сохранены в основе main a543fa8.

16.10.2026, PR #15: [личные XP, уровни, разрешения и вкладка развития](docs/COMPANY_PROGRESSION.md), 0.3.7-westded.2-experimental/protocol 7. Код/миграция/UI/scheduler проверены offline, игровые здания и полный save/restart ещё NOT TESTED. Следующий абзац описывает предыдущий PR #14.

16.10.2026: [offline исправления](docs/OFFLINE_UPSTREAM_FIXES.md), 0.3.7-westded.1-experimental/protocol 6, поверх PR #12: данные/waypoints, indexed refs/rotated checks, очереди покупок/edits, unique load identity, speed/notice UI и bounded notification preferences. В документе — полная матрица выполненного и отложенного для двигателя. Ниже историческое состояние #11; новые игровые проверки ещё не выполнены.

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
| Прогресс/разрешения | PR #15: личные saved XP/rank, штатные permits/multipliers и explicit promotion | Core/adapter/scheduler/UI fixtures PASS; настоящий game XP, цены и сохранение ещё NOT TESTED |
| Штаб/уникальные здания | Подсчёт построек по владельцу, личный уровень для открытий; census failure ограничителен | Общий permit механизм проверен offline; настоящий штаб/Колизей/улучшения требуют игры |
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
