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
| Контракты | Upstream owner/filter wrappers | Настоящие offers/rewards/delivery не проверены |
| Конкуренция грузов/пассажиров | Используются штатные economy/pathfinding | Поведение разных компаний не измерено; новой искусственной экономики нет |
| Платная инфраструктура | Upstream shared station/per-stop fee | Пользователи сообщили о недоступности paid stops; UI fixes в PR #11 ещё требуют game повтора; stable visit ID/пути/депо не решены |
| Центральный порядок и epochs | Offline journal/cursor/quorum реализованы | Классические file/replay tests; **не подключены к игровому scheduler** |
| Владелец после перезапуска | Owner fingerprint сохраняется с company id; RSA key в LocalAppData, explicit legacy assignment | Python registry roundtrip + реальные C# файлы/TCP/net48; настоящий save/restart пока NOT RUN |
| Полная Windows сборка | Обязательная CI job | net48 EXE, x64 DLL, 14 exports/ordinals, ZIP/license; hooks не запускались |
| Игровой каталог ресурсов | Исправлен в PR #10: permissions module зарегистрирован; harness учитывает catalog | Реальный ZIP/copy hashes и Lua bootstrap fixture PASS; первый пользовательский game startup FAIL, повтор с исправлением NOT RUN |
| Локализация | 33 gaps заполнены | 227 используемых catalog keys в 11 локалях, placeholders проверяются; диагностика новых компонентов может быть на английском |
| Gigantomaniac | Цель будущего этапа | Ни карты, ни saves/RAM/game step profiling нет |

История принятых исправлений: [#2](https://github.com/WestDed/TransportFever3-Multiplayer/pull/2) → [#3](https://github.com/WestDed/TransportFever3-Multiplayer/pull/3) → [#4](https://github.com/WestDed/TransportFever3-Multiplayer/pull/4) → [#5](https://github.com/WestDed/TransportFever3-Multiplayer/pull/5) → [#6](https://github.com/WestDed/TransportFever3-Multiplayer/pull/6). Документы продолжения находятся в docs/STAGE1_*.md. Исторический [аудит функций](https://github.com/WestDed/TransportFever3-Multiplayer/blob/6611e8f63e29e04aeb978bddd17877b461894edf/FEATURES.md) остаётся доступен.

PR #8 слит заказчиком в main 5f6cd58. [PR #9](https://github.com/WestDed/TransportFever3-Multiplayer/pull/9) исправляет ambiguity pending owner, частичный roster commit/stale authority, sid refresh и invalid host/world assignment. [Разбор и результаты](docs/MAIN_REVIEW_2026_10_13.md); фактическое слияние PR #9 проверять через GitHub.

## Обновление после совместной игры — PR #11

| Функция | Состояние | Предел проверки |
|---|---|---|
| Bulk transfer и control messages | Исправлен premature disconnect при заполнении очереди; отдельный receipt/load wait | Real queue/TCP/net48 tests; большая карта в игре ещё требует повтора |
| Выбор платной остановки | Исправлены construction-owner fallback, cache, поздний module load и UI reload context | Real Lua functions с injected facts; selection/line/vehicle в игре NOT RETESTED |
| Одна городская субсидия — один владелец | Guard existing/unclaimed/ready, closed/retry/ambiguous UID denial, saved claim | Component regressions; native ordering, reward/delivery и simultaneous click не приняты |
| Чужие карточки субсидий | Исправлен индекс при отсутствующих списках | Component regressions; все остальные notices не закрыты |
| Право строить вдоль чужой дороги | Не реализовано полноценно | Следующий engine permission gate |
| Платные дороги | Новая запланированная механика | Нет working tariff/payment UI или stable passage ledger |
| Собственные субсидии | Не реализованы | Межфирменные заказы с escrow планируются отдельно |
| Чужие денежные надписи/общие company notices | Пользовательский отказ, диагностика нужна | Native ABI не меняется без свежих данных; UI privacy ещё не принята |
