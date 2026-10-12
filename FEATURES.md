# Состояние функций

13.10.2026. «Реализовано в коде» и «проверено без игры» не означают готовность функции в Transport Fever 3. Установленной игры, Steam-сессии и настоящих saves нет. Стабильный выпуск не подготовлен.

| Область | Состояние кода | Подтверждение и предел |
|---|---|---|
| Хост одновременно играет/служит сервером | Существующая архитектура MPFever | Relay/Client и сборка испытаны; игра/WAN/NAT не испытаны |
| Безопасный декодер | Исправлено | Реальные Lua/C# parser tests, limits; Lua expressions не исполняются |
| TCP/IPC и медленный peer | Ограничены | Настоящие sockets, fragmented/oversized/timeout, independent queues; memory budget игры не принят |
| Граница принятия TCP-клиента | Broadcast/адресная отправка только Joined; connection первым; 4 КиБ handshake | Real TCP regressions в PR #6; initial identity/TLS ещё отсутствуют |
| Снимок мира | Потоковый hash/atomic commit/cleanup | Сгенерированный файл 32 MiB, 1/4/8 peers; настоящий save/load не испытан |
| Переподключение | Credential, accepted handshake, full-server replacement | Real TCP; только текущая host session, без Steam proof/host migration |
| Реестр отдельных компаний | Upstream + исправленный sid/start signature | Pure Lua match/join/serialize/collision tests; PLAYER entities в игре не проверялись |
| Право указать co | Host roster/session binding и schema | C# negative tests; roster timing/межсессионный owner требуют engine/auth gate |
| Флот и линии | Дополнительная проверка resolved owner | Lua policy с injected ownership; native graph/terrain/assets и разрешённые refs неполны |
| Бюджет и кредиты | Частично upstream; Repay берёт сохранённые terms | Real coLoanEvent с mock dependencies; Obtain/месячная схема/формула game loan_util не проверены |
| Прогресс/разрешения | Существующий общий progression fallback | Независимый прогресс ещё не реализован/не подтверждён |
| Контракты | Upstream owner/filter wrappers | Настоящие offers/rewards/delivery не проверены |
| Конкуренция грузов/пассажиров | Используются штатные economy/pathfinding | Поведение разных компаний не измерено; новой искусственной экономики нет |
| Платная инфраструктура | Upstream shared station/per-stop fee | Stable visit ID, reroute/reload/double billing не решены; пути/депо по договорам отсутствуют |
| Центральный порядок и epochs | Offline journal/cursor/quorum реализованы | Классические file/replay tests; **не подключены к игровому scheduler** |
| Полная Windows сборка | Обязательная CI job | net48 EXE, x64 DLL, 14 exports/ordinals, ZIP/license; hooks не запускались |
| Локализация | 33 gaps заполнены | 227 используемых catalog keys в 11 локалях, placeholders проверяются; диагностика новых компонентов может быть на английском |
| Gigantomaniac | Цель будущего этапа | Ни карты, ни saves/RAM/game step profiling нет |

История принятых исправлений: [#2](https://github.com/WestDed/TransportFever3-Multiplayer/pull/2) → [#3](https://github.com/WestDed/TransportFever3-Multiplayer/pull/3) → [#4](https://github.com/WestDed/TransportFever3-Multiplayer/pull/4) → [#5](https://github.com/WestDed/TransportFever3-Multiplayer/pull/5) → [#6](https://github.com/WestDed/TransportFever3-Multiplayer/pull/6). Документы продолжения находятся в docs/STAGE1_*.md. Исторический [аудит функций](https://github.com/WestDed/TransportFever3-Multiplayer/blob/6611e8f63e29e04aeb978bddd17877b461894edf/FEATURES.md) остаётся доступен.

Общее содержимое #3–#6 ещё переносится из промежуточных веток в main через [PR #7](https://github.com/WestDed/TransportFever3-Multiplayer/pull/7). Закрытый статус PR не означает наличие всех следующих изменений в main: [проверка слияний](docs/INTEGRATION_STAGE1.md).
