# Продолжение проекта в следующей сессии

Основной репозиторий: https://github.com/WestDed/TransportFever3-Multiplayer. Пользователь разрешил автономную работу без игры, отдельные ветки и PR, без автоматического merge. GitHub write доступ восстановлен; Desktop не требуется. Авторские репозитории не изменяем, MIT copyright arthur сохранён.

## Сохранённая работа

- PR #1 — аудит, объединён заказчиком. Main базы: 6611e8f63e29e04aeb978bddd17877b461894edf; upstream 0.3.6: 0c4a6d35bb5fd22f2ef851d8089e4c6f2cde6773.
- [PR #2](https://github.com/WestDed/TransportFever3-Multiplayer/pull/2), `fix/offline-tests-safe-decoder`, base main. Реальные 17 lockstep assertions, no Lua eval, bounded C# parser, strict CI. Head 99ccf249; 44 Python tests с тогдашним AUD-04, 46 C# checks.
- [PR #3](https://github.com/WestDed/TransportFever3-Multiplayer/pull/3), `fix/network-bounds-snapshots`, base #2. Bounded TCP/C# IPC/outboxes, streamed SHA/atomic snapshots, full Windows build/license/portable ZIP. Head 87102063; run 38209145412, 6 jobs green, 70 C# component checks.
- [PR #4](https://github.com/WestDed/TransportFever3-Multiplayer/pull/4), `fix/company-identity-schemas`, base #3. sid signature, credential reconnect, directions/schema/co binding, fleet/line policy, stored Repay. Head b3fc4108; run 38210479835, 6 jobs green, 60 Python tests без expectedFailure.
- [PR #5](https://github.com/WestDed/TransportFever3-Multiplayer/pull/5), `fix/offline-order-recovery`, base #4. Lua IPC, durable journal/cursor/barrier foundation, real TCP bench/slow peer, full-server accepted reconnect, native export checks, translation gaps filled, allocation fixes and lifecycle guards. Точный итоговый head/run смотреть в PR, не считать прежний SHA доказательством позднего изменения.

- [PR #6](https://github.com/WestDed/TransportFever3-Multiplayer/pull/6), `fix/handshake-isolation`, base #5. Pending sockets не получают общие/адресные данные; connection enqueue до Joined; first-frame cap 4 КиБ без потери buffered traffic; real TCP regressions и 32 reconnect при concurrent broadcast. Точный CI head/run смотреть в PR. Initial identity/TLS не реализованы.

Прочитать ARCHITECTURE/FEATURES/ROADMAP/RISKS/TESTING, docs/STAGE1_*.md и ADR_0001_COMMAND_CUTOVER.md. Не повторять уже выполненные fixes. Все branches stack; для review порядок #2 → #3 → #4 → #5 → #6. После ручного слияния предыдущей ветки проверить diff следующего PR относительно актуальной main и при необходимости retarget; не force-push чужой head и не merge самостоятельно.

## Граница завершения

Игры/бинарников/saves/Steam-сессии нет. Полная EXE/DLL сборка, parser/socket/file tests — выполнены; игровые tests не выполнялись. CommandJournal/ReplayCursor/RecoveryBarrier **не используются production scheduler**. Первое подтверждение старой company ownership, TLS/initial admission, полные schemas/ценовые правила/native world rights и save/frontier cutover ещё не готовы. Shared progression/Obtain/billing/конкуренция/Gigantomaniac остаются.

Следующий код — лишь отдельным обоснованным PR. До engine adapter сначала получить реальные game build/API/saves и пройти минимальный стенд. Можно продолжать source review/fuzz/security design/offline components, но не выдавать их за доказанную игру и не включать central replay без GUI/native hold gate.

Локальные scratch пути не являются постоянным хранилищем: восстановить ветки из GitHub. Все необходимые исходники/тесты/документы опубликованы там; параметры воспроизведения в TESTING.md. Benchmark JSON постоянно в docs/benchmarks, текущие результаты/ZIP/manifest дополнительно в Actions artifacts. Ни один PR не был объединён автоматически.
