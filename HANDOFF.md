# Продолжение проекта в следующей сессии

Основной репозиторий: https://github.com/WestDed/TransportFever3-Multiplayer. Пользователь разрешил автономную работу без игры, отдельные ветки и PR, без автоматического merge. GitHub write доступ восстановлен; Desktop не требуется. Авторские репозитории не изменяем, MIT copyright arthur сохранён.

## Текущее состояние 15.10.2026

Main `937318b7747eaf9048a3ebf35d30efd4d211ea0c`, tree `c595c46a48f7e7f277b0a4e78c04647cd5579037`: PR #10 объединён пользователем. После исправления catalog и настройки Porthole пользователи сообщили об успешном совместном запуске. Porthole — TCP proxy: host предлагает localhost TCP 28090, guest принимает и вручную подключается к своему localhost. Не посылать их обратно на Radmin IP или Steam public-IP invite при этой схеме.

Первые игровой проблемы PLAY-01–08 и все требования пользователя сохранены в [COOP_PLAYTEST_2026_10_15](docs/COOP_PLAYTEST_2026_10_15.md). Большой save: долго, reconnect около 50%; дороги: нельзя строить возле чужих, нет owner grants/tolls; paid stops недоступны; городскую subsidy берут оба; custom subsidies отсутствуют; foreign notices/money popups видны.

[PR #11](https://github.com/WestDed/TransportFever3-Multiplayer/pull/11), ветка `fix/first-coop-gameplay-2026-10-15`, непосредственно от current main и прямо в main. Отдельные logical commits/tests: subsidy one-claim guard и list-hole private cards; station UI/SIM owner resolution, live cache invalidation, late module retry и fresh reload context; reserved outbox capacity для control без изменения FIFO/8 MiB/128 frames, verified receipt перед load budget, bounded deadlines и diagnostics. No new native ABI/world economics/custom contracts. Roads/custom subsidies/general privacy не объявлять исправленными.

Code head `ebd3395f5e14b41d37e809a65aa0c6a4d4c36b84`, run `38591306735`: все семь jobs green; 105 Python/Lua, 336 C# checks в каждой net8 Linux/Windows/actual net48 Windows, 50 decoder, TCP bench, полный actual Windows ZIP и hashes всех 15 game resources. Baseline `2f570c09` / run `38590443119` воспроизвёл queue control refusal на всех C# runtime и Lua defects. Exact final documentation head/run смотреть в PR #11. Game fixes NOT RETESTED.

Следующий шаг: сверить статус PR #11/main, одинаковый полный новый пакет на обоих игровых ПК, повтор большой карты и paid-stop selection/route/vehicle. Для точной причины 50% получить fresh launcher logs обоих, новые session folders и размер save; уточнить, 50% transfer или engine load. Для general notices/popups получить точный тип/screenshot/native/UI logs. Private identity profiles никогда не запрашивать.

User владелец critical: настоящий owner restore после game save/host restart по-прежнему не принят. Эту проверку сохранить в game matrix. Main изменяет/PR merges делает только пользователь; новый PR после merge начинать от свежей main. Игровой runtime в среде разработчика отсутствует; не возвращаться к устаревшему утверждению, что пользователи ещё не купили игру.

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
