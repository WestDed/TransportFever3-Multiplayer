# План разработки и критерии готовности

Основа — существующий MPFever, не переписывание с нуля. PR #8 объединён в main 5f6cd58: постоянный владелец и net48 execution. [PR #9](https://github.com/WestDed/TransportFever3-Multiplayer/pull/9) исправляет roster/ownership проверки этой main. Main меняет только заказчик после review; следующие задачи — отдельной веткой от актуальной main с PR в main. Сроки до доступа к движку не обещаем.

## Этап 0 — аудит

Выполнен и объединён заказчиком в PR #1: лицензии/версии/архитектура/состояние, первые pure tests. Архив результатов в Git history main 6611e8f. Выбор MPFever обоснован близкой Lua/native/host-PC архитектурой; альтернативный Rust/QUIC сервер не устраняет автоматически engine/order/company issues.

## Этап 1 — компоненты без игры

В PR выполнены: восстановление всех 17 upstream lockstep assertions, data-only Lua/C# decoding, строгий CI без expectedFailure/translation exemptions, bounded TCP/Lua/C# IPC/outboxes, protocol schemas, sid signature, session reconnect, host-reported co binding, ограниченная fleet/line policy, stored Repay terms, streaming SHA256/atomic snapshot, Windows build/exports/package, реальный TCP load/fault стенд и durable order/replay/recovery foundation.

Критерии этой offline части: suite и build проходят на точном PR head; нет новых неожиданных failures; malformed/old epoch/duplicate/gap/corruption дают ограниченный отказ; slow peer не блокирует healthy broadcast; torn journal tail восстанавливается; failed recovery не допускает resume. Нагрузочные JSON сохраняются без game claims.

PR #8 реализует межсессионный owner по persistent private key и host-only legacy migration без доверия имени/sid; включает actual Framework runtime tests. Criterion: после разрушения endpoints/session maps повторное чтение key и saved registry восстанавливает тот же co; replay/foreign key/token/ambiguous owner/corrupt profile отказывают; restored company не получает стартовый капитал снова. Engine save/restart пока NOT RUN.

**Весь этап 1 ещё не закрыт.** Остаются: initial host admission/TLS/server authentication и игровое подтверждение owner migration/save-load; полные engine command/ref/price schemas; actual central replay/epoch cutover; freeze tools/drain callbacks и save/frontier atomicity. Это не следует скрывать зелёным CI. Исследование engine API и подтверждение этих переходов требует установленной игры; проект не объявляется ready.

## Этап 2 — минимальный игровой стенд

Зафиксировать build/hash игры, EXE/DLL/mods/DLC, machines, saves/seed. Сначала два процесса одного ПК, затем два ПК. Unknown game build/hooks — fail closed. Проверить upstream #15–22: снос станции, сигналы/туннели/мосты, модульные правки, копирование состава и callbacks линий. Пауза/x1/x4, simultaneous builds, десять повторений каждого сценария. Сверять геометрию/owners/balances, не только checksum. Два часа без unexplained desync/crash — стартовый порог.

## Этап 3 — независимость компаний и включение порядка

Выполнить [ADR cutover](docs/ADR_0001_COMMAND_CUTOVER.md) на подтверждённых hooks. Matrix: own/foreign construction, deletion, fleet, routes, loans, contracts, progression, bankruptcy. Один принятый command/result/payment, dependent create/update после callbacks, issuer получает общий replay. Полная история финансов, один корректный плательщик без маскировки correctMoney. При недостатке средств/отказе нет лишнего списания. Shared progression не считается независимостью.

## Этап 4 — конкуренция и инфраструктура

Сначала измерить штатный pathfinding: один завод/город, две компании, цены/интервалы, пересадки. Проверять stocks/onboard/deliveries/revenues по сегментам, без дублирования. Затем договоры путей/станций/депо, доступ/плата/смена владельца/банкротство. Stable visit ID, одна оплата за посещение, сумма межфирменных переводов ноль, reroute/reload сохраняют результат. Не создавать новую экономику до измерения штатной.

## Этап 5 — recovery и длительность

Разрыв в request/flush/delivery/callback/save/chunk/load; crash host/client, late ack/old epoch, same names, full-server reconnect, slow peer. 20 **игровых** циклов без loss/double apply/payment. Failure остаётся frozen; corruption не заменяет final save. 4 игрока/4 часа, затем 8-часовой soak перед релизом. Host migration — отдельная задача, сейчас отсутствует.

## Этап 6 — большие карты

Настоящие saves от малого размера до Gigantomaniac; game step/s, checksum p50/p95, RAM, save/transfer/load, upload хоста. Loopback 32 MiB не оценивает карту. Предварительные цели: сетевой слой <10% потери steps/s относительно solo того же save, checkpoint p95 <100 мс, без OOM; параметры/конфигурация согласуются после измерений. При провале profiling — dirty sets/incremental hashes/stream channel отдельными PR.

Смена основы — только отдельный ADR с игровыми доказательствами: дрейф при одинаковых inputs/builds, неисправимые hooks, неприемлемый reload overhead либо несовместимость модели компаний со штатной экономикой. Зелёные mocks или обещания другой реализации этого не доказывают.
