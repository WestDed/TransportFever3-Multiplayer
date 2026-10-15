# Продолжение проекта в следующей сессии

Основной репозиторий: https://github.com/WestDed/TransportFever3-Multiplayer. Пользователь разрешил автономную работу без игры, отдельные ветки и PR, без автоматического merge. GitHub write доступ восстановлен; Desktop не требуется. Авторские репозитории не изменяем, MIT copyright arthur сохранён.

## Текущее состояние 15.10.2026

Main `937318b7747eaf9048a3ebf35d30efd4d211ea0c`, PR #10 объединён пользователем. После catalog fix/Porthole пользователи действительно сыграли вместе. Porthole — TCP proxy: host предлагает localhost TCP 28090, guest принимает и вручную подключается к своему localhost. Не возвращать их на Radmin IP/Steam public-IP invite для этой схемы.

[PR #11](https://github.com/WestDed/TransportFever3-Multiplayer/pull/11) открыт в `fix/first-coop-gameplay-2026-10-15`, прямо в main; merge не делался. Пользователь потребовал реализовать **все** замечания PLAY-01–08 до следующего игрового теста. Уточнил: 50% **на экране загрузки самой игры у брата**, после transfer. Не спрашивать это ещё раз. Новые механики/интерфейс не оставлены только в плане.

Читать [BUSINESS_MECHANICS](docs/BUSINESS_MECHANICS.md), [историю первого co-op](docs/COOP_PLAYTEST_2026_10_15.md), ARCHITECTURE/FEATURES/ROADMAP/RISKS/TESTING. Реализованы: saved use/connect/edit ACL, private/free/toll road/station modes, stable road geometry/visit billing, saved debit/credit queue и escrow, create/accept/deliver/cancel/expire orders, GUI company-notification filter и money projection после replication, launcher heartbeat/ready с конкретным snapshot. Новый пакет **0.3.7-experimental**, **protocol 5**, 19 ресурсов; обновлять полный набор у обоих.

Старый пакет 0.3.6/run38592584484 не содержит всей этой работы. Первый новый полный run38683104340: Windows build/native test, decoder, обе Lua jobs PASS; C# compile FAILED из-за коллизии now в тесте, исправлена. Локально после исправления .NET8 production components: 363 PASS; Python suite и окончательные CI/head/artifact смотреть в PR #11. Не выдавать промежуточный красный run за финальную проверку.

Следующий шаг после green exact head — пользовательский игровой повтор всей восьмипунктовой матрицы BUSINESS_MECHANICS, исправление реальных API/native incompatibilities. Всё новое **NOT RETESTED**. Owner restore после настоящего save/host restart по-прежнему обязательный game gate. Critical limits: submitted booking становится uncertain после reload, автоматическая сверка native journal не доказана; public road ACL зависит от возможности отменить author proposal до apply; native UI filter отказывает при source/destination alias; Gigantomaniac ещё не профилирован. Не заявлять central CommandJournal cutover/TLS/host migration готовыми.

GitHub write доступ есть; Desktop не нужен. Только ветки/PR, main меняет пользователь; после merge новая задача от свежей main. MIT copyright arthur сохранён, upstream untouched. У разработчика нет локального игрового runtime; у заказчика и брата игра уже есть и они провели испытание. Private identity files не запрашивать/не публиковать; save не кладут в identity.

## История сохранённой работы

- PR #1 — аудит, объединён заказчиком. Main базы: 6611e8f63e29e04aeb978bddd17877b461894edf; upstream 0.3.6: 0c4a6d35bb5fd22f2ef851d8089e4c6f2cde6773.
- [PR #2](https://github.com/WestDed/TransportFever3-Multiplayer/pull/2), `fix/offline-tests-safe-decoder`, base main. Реальные 17 lockstep assertions, no Lua eval, bounded C# parser, strict CI. Head 99ccf249; 44 Python tests с тогдашним AUD-04, 46 C# checks.
- [PR #3](https://github.com/WestDed/TransportFever3-Multiplayer/pull/3), `fix/network-bounds-snapshots`, base #2. Bounded TCP/C# IPC/outboxes, streamed SHA/atomic snapshots, full Windows build/license/portable ZIP. Head 87102063; run 38209145412, 6 jobs green, 70 C# component checks.
- [PR #4](https://github.com/WestDed/TransportFever3-Multiplayer/pull/4), `fix/company-identity-schemas`, base #3. sid signature, credential reconnect, directions/schema/co binding, fleet/line policy, stored Repay. Head b3fc4108; run 38210479835, 6 jobs green, 60 Python tests без expectedFailure.
- [PR #5](https://github.com/WestDed/TransportFever3-Multiplayer/pull/5), `fix/offline-order-recovery`, base #4. Lua IPC, durable journal/cursor/barrier foundation, real TCP bench/slow peer, full-server accepted reconnect, native export checks, translation gaps filled, allocation fixes and lifecycle guards. Точный итоговый head/run смотреть в PR, не считать прежний SHA доказательством позднего изменения.

- [PR #6](https://github.com/WestDed/TransportFever3-Multiplayer/pull/6), `fix/handshake-isolation`, base #5. Pending sockets не получают общие/адресные данные; connection enqueue до Joined; first-frame cap 4 КиБ без потери buffered traffic; real TCP regressions и 32 reconnect при concurrent broadcast. Точный CI head/run смотреть в PR. Initial identity/TLS не реализованы.

Прочитать ARCHITECTURE/FEATURES/ROADMAP/RISKS/TESTING, docs/STAGE1_*.md, MAIN_REVIEW_2026_10_13.md и ADR_0001_COMMAND_CUTOVER.md. Не повторять уже выполненные fixes. Предыдущие branches/heads/runs выше — исторические доказательства. Исходный итоговый head #6 683328da прошёл run 38318567426: 67 Python/Lua, 276 C# checks на Ubuntu/Windows, 50 LuaLit, full Windows build; 6 jobs green.

## Граница завершения

В среде разработчика игры/бинарников/saves/Steam-сессии нет. Полная EXE/DLL сборка и component tests выполнены; пользователи выполнили ограниченный игровой запуск, а исправления PR #11 ещё не повторили в игре. CommandJournal/ReplayCursor/RecoveryBarrier **не используются production scheduler**. Persistent key ownership и explicit legacy migration реализованы в PR #8 и проверяются offline; реальная engine persistence/migration, TLS/initial admission, полные schemas/ценовые правила/native world rights и save/frontier cutover ещё не подтверждены. Shared progression/Obtain/billing/конкуренция/Gigantomaniac остаются.

Следующий код — лишь отдельным обоснованным PR. До engine adapter сначала получить реальные game build/API/saves и пройти минимальный стенд. Можно продолжать source review/fuzz/security design/offline components, но не выдавать их за доказанную игру и не включать central replay без GUI/native hold gate.

Локальные scratch пути не являются постоянным хранилищем: восстановить ветки из GitHub. Все необходимые исходники/тесты/документы опубликованы там; параметры воспроизведения в TESTING.md. Benchmark JSON постоянно в docs/benchmarks, текущие результаты/ZIP/manifest дополнительно в Actions artifacts. Ни один PR не был объединён автоматически.
