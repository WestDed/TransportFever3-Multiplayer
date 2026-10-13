# Продолжение проекта в следующей сессии

Основной репозиторий: https://github.com/WestDed/TransportFever3-Multiplayer. Пользователь разрешил автономную работу без игры, отдельные ветки и PR, без автоматического merge. GitHub write доступ восстановлен; Desktop не требуется. Авторские репозитории не изменяем, MIT copyright arthur сохранён.

## Текущее состояние 13.10.2026

PR #8 объединён пользователем в main `5f6cd5845adb2cc59e49acfc436e77d80668994f`, tree e5ef8214. [Main run 38346446265](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38346446265): все 7 jobs green, 78 Python/Lua и 306 C# checks. PR #7 ранее слит в main 47604fbb. Старые #2–#8 повторно не сливать; история промежуточных merge в docs/INTEGRATION_STAGE1.md.

[PR #8](https://github.com/WestDed/TransportFever3-Multiplayer/pull/8), `fix/persistent-company-ownership`, создан от этой main и направлен прямо в main. Постоянные RSA-2048 host/client profiles в LocalAppData/MPFever/identity, owner fingerprint в сохранённом registry, signed fresh challenge (protocol 4), owner-based CompanyBindings/Lua lookup, host-only legacy assignment по world id и отказ при потере/неожиданной замене key. README/INSTALL и docs/STAGE1_PERSISTENT_OWNERSHIP.md содержат backup/migration/limitations. Не отправлять private profiles в логи или GitHub.

Включено actual net48 execution тех же C# tests в Windows CI. Первый run 38339664174 поймал Framework CSP/KeySize bug; исправлен явным provider/constructor. Следующий run 38340158625: все 7 jobs green. Последующие изменения проверять по точному финальному head/run PR #8, не переносить старый результат.

[PR #9](https://github.com/WestDed/TransportFever3-Multiplayer/pull/9), `fix/ownership-review`, непосредственно от main 5f6cd58 и прямо в main. Полная owner uniqueness даже pending rows, атомарный roster/binding commit и запрет stale authority после invalid report; sid metadata не меняет proven owner; Lua assignment требует valid host/world. [Разбор, воспроизведение и ошибки фикстур](docs/MAIN_REVIEW_2026_10_13.md). Code head c5ae9be7 прошёл run 38348129418: все 7 jobs green, 79 Python/Lua, 323 C# checks на net8 Linux/Windows и actual net48 Windows, 50 decoder checks, полный Windows build. После него добавлена документация; точный финальный head/run смотреть в PR #9.

Сначала проверить текущий статус PR #9/main. Если слит, новую задачу начинать веткой от актуальной main и направлять PR прямо в main. Не объединять автоматически, не force-push чужую ветку. User считает восстановление владельца критическим; реальный игровой save/host restart по-прежнему NOT RUN и обязателен.

## История сохранённой работы

- PR #1 — аудит, объединён заказчиком. Main базы: 6611e8f63e29e04aeb978bddd17877b461894edf; upstream 0.3.6: 0c4a6d35bb5fd22f2ef851d8089e4c6f2cde6773.
- [PR #2](https://github.com/WestDed/TransportFever3-Multiplayer/pull/2), `fix/offline-tests-safe-decoder`, base main. Реальные 17 lockstep assertions, no Lua eval, bounded C# parser, strict CI. Head 99ccf249; 44 Python tests с тогдашним AUD-04, 46 C# checks.
- [PR #3](https://github.com/WestDed/TransportFever3-Multiplayer/pull/3), `fix/network-bounds-snapshots`, base #2. Bounded TCP/C# IPC/outboxes, streamed SHA/atomic snapshots, full Windows build/license/portable ZIP. Head 87102063; run 38209145412, 6 jobs green, 70 C# component checks.
- [PR #4](https://github.com/WestDed/TransportFever3-Multiplayer/pull/4), `fix/company-identity-schemas`, base #3. sid signature, credential reconnect, directions/schema/co binding, fleet/line policy, stored Repay. Head b3fc4108; run 38210479835, 6 jobs green, 60 Python tests без expectedFailure.
- [PR #5](https://github.com/WestDed/TransportFever3-Multiplayer/pull/5), `fix/offline-order-recovery`, base #4. Lua IPC, durable journal/cursor/barrier foundation, real TCP bench/slow peer, full-server accepted reconnect, native export checks, translation gaps filled, allocation fixes and lifecycle guards. Точный итоговый head/run смотреть в PR, не считать прежний SHA доказательством позднего изменения.

- [PR #6](https://github.com/WestDed/TransportFever3-Multiplayer/pull/6), `fix/handshake-isolation`, base #5. Pending sockets не получают общие/адресные данные; connection enqueue до Joined; first-frame cap 4 КиБ без потери buffered traffic; real TCP regressions и 32 reconnect при concurrent broadcast. Точный CI head/run смотреть в PR. Initial identity/TLS не реализованы.

Прочитать ARCHITECTURE/FEATURES/ROADMAP/RISKS/TESTING, docs/STAGE1_*.md, MAIN_REVIEW_2026_10_13.md и ADR_0001_COMMAND_CUTOVER.md. Не повторять уже выполненные fixes. Предыдущие branches/heads/runs выше — исторические доказательства. Исходный итоговый head #6 683328da прошёл run 38318567426: 67 Python/Lua, 276 C# checks на Ubuntu/Windows, 50 LuaLit, full Windows build; 6 jobs green.

## Граница завершения

Игры/бинарников/saves/Steam-сессии нет. Полная EXE/DLL сборка, parser/socket/file tests — выполнены; игровые tests не выполнялись. CommandJournal/ReplayCursor/RecoveryBarrier **не используются production scheduler**. Persistent key ownership и explicit legacy migration реализованы в PR #8 и проверяются offline; реальная engine persistence/migration, TLS/initial admission, полные schemas/ценовые правила/native world rights и save/frontier cutover ещё не подтверждены. Shared progression/Obtain/billing/конкуренция/Gigantomaniac остаются.

Следующий код — лишь отдельным обоснованным PR. До engine adapter сначала получить реальные game build/API/saves и пройти минимальный стенд. Можно продолжать source review/fuzz/security design/offline components, но не выдавать их за доказанную игру и не включать central replay без GUI/native hold gate.

Локальные scratch пути не являются постоянным хранилищем: восстановить ветки из GitHub. Все необходимые исходники/тесты/документы опубликованы там; параметры воспроизведения в TESTING.md. Benchmark JSON постоянно в docs/benchmarks, текущие результаты/ZIP/manifest дополнительно в Actions artifacts. Ни один PR не был объединён автоматически.
