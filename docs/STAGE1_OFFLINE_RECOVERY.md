# Этап 1: восстановление, реальные сокеты и предел доказательства

[PR #5](https://github.com/WestDed/TransportFever3-Multiplayer/pull/5), `fix/offline-order-recovery`, зависит от #4 → #3 → #2. Без автоматического merge. TF3 не устанавливалась; используется сгенерированный файл, не игровой save.

## Применённые исправления

- Lua file IPC ограничен порциями 256 KiB и незавершённой строкой 4 MiB + 256 B; oversized пропускается до LF, partial/UTF-8 сохраняются, truncate сбрасывает offset. Поиск последнего `resync_load` просматривает log порциями, без ReadAllText. Ограниченная память не означает ограниченное общее время сканирования большого лога.
- Client.Connect завершает TCP и принятие входа с deadlines 10 с; отказ в версии/credential/capacity является ошибкой Connect. Буфер первого reader передаётся read loop, последующие сообщения не теряются. Repeated connection credential отвергается. Bare TCP больше не сбрасывает цикл auto-reconnect как успешный вход.
- Восемь **принятых игроков** и четыре reserve handshakes. Новый девятый игрок отвергается; правильный credential допускает замену старого сокета при полном сервере. Core Relay сам отключает старую identity, не ждёт обработчика формы. Резерв ограничен и имеет тот же handshake deadline.
- Старые host callbacks/auto-start/start timer проверяют session generation; auto-start больше не начинает сессию после 120-секундного timeout. Проверки generation не заменяют атомарный lifecycle и engine epoch fence; эти UI/engine переходы ещё требуют испытаний.
- `SetSpeed` запрещает resume при активном/неудачном resync. Failure сохраняется до успешного recovery либо окончания сессии. Новые actions на границе save всё ещё требуют ADR cutover; только ограничить скорость недостаточно.
- Удалены лишние большие копии строки при envelope/encoding/base64 serialization. Literal без escapes проверяется и берётся одним substring; control/length лимиты сохранены. Канонический base64 проверяет alphabet/padding/unused bits без повторного большого Encode. DEL в LuaLit.Quote теперь экранируется.
- Заполнены 33 пропуска переводов: три ключа в 11 локалях. В текущем code scan 227 используемых ключей — 227/227 в каждой локали; шесть прежних неиспользуемых ключей сохранены. Нет исключений для missing translations или expectedFailure.
- Native build проверяет AMD64 PE, 14 имён/ordinal из исходного winhttp.def, отсутствие CRT dependency, zip entries и неизменённую MIT license. DLL не загружается в игру и hooks не исполняются.

## Подготовка центрального исполнения

`CommandJournal`, `ReplayCursor`, `RecoveryBarrier` реализованы и проверены как отдельные компоненты; **не подключены к игровому scheduler**. Подробный формат, свойства и путь включения — [ADR 0001](ADR_0001_COMMAND_CUTOVER.md).

Проверки используют действующие C# файлы и real file/sockets: concurrent authors, duplicate/conflicting uid, epoch/co/schema, replay gap, broken hash/length, torn tail, reopen, durable freeze, quorum/late ack/failure. Двадцать offline replay cycles проверяют frontier/дубликаты чистого компонента; это не двадцать игровых reconnect. Ownership/loan tests исполняют реальный Lua с injected engine dependencies; настоящих PLAYER/ACCOUNT/pathfinding нет.

## Фактические проверки и производительность

На [run 38213663673](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38213663673) прошли все шесть jobs: **67 Python/Lua tests**, **210 C# component checks** на Ubuntu и Windows, **50 LuaLit checks**, full net48 launcher/native x64 build/package/exports. Финальная проверка после последующих изменений фиксируется в PR по точному head.

Bench использует 32 MiB случайных байтов и 1/4/8 клиентов через реальные Relay/Client/outboxes/receiver/atomic commit. Каждый destination дополнительно проверяется SHA-256. Slow reader проверяется и детерминированным blocking stream, и настоящим non-reading TCP peer; здоровый peer получает следующий broadcast.

[Сырые baseline/после удаления копий](benchmarks/tcp-loopback-2026-10-12.json) сохранены с Actions run IDs. На восьми клиентах baseline занял Windows 4.96 с / 574 MiB RSS, Ubuntu 3.98 с / 514 MiB RSS. Следующий прогон — Windows 4.74 с / 693 MiB RSS, Ubuntu 3.99 с / 475 MiB RSS. **Снижение RSS не доказано**: сборщик мусора, порядок прогонов и разные hosted runners влияют на числа. Нет принятого memory budget для production или Gigantomaniac.

`AggregateMiBPerSecond` считается по декодированным snapshot bytes, не по размеру wire/base64. Timer исключает создание source, первоначальный hash и handshakes; включает отправку, приём, hashes клиентов и запись. Peak WorkingSet — sampling раз в 20 мс всего net8 console процесса. Native game CPU/RAM, WAN, latency/loss и размер настоящего save не измерялись. Это не гарантированная скорость для игроков.

## Воспроизведение

```sh
python -m pip install -r tests/requirements-offline.txt
python -m unittest discover -s tests -p 'test_*.py' -v
python tests/test_lockstep.py
python tests/test_lang.py
dotnet run --project tests/offline/Offline.csproj --configuration Release
dotnet run --project tests/offline/Offline.csproj --configuration Release -- --bench
```

Windows: `./tests/test_lualit.ps1`, `cmd /c build.bat`, `./tests/verify_windows_build.ps1`. VS C++/MSBuild/.NET 4.8 targeting pack необходимы для build; .NET 8 SDK — только для component suite. GitHub Actions поставляет среду, локально Linux запуск игры/WinForms не предпринимался.

Workflow обязателен на каждом PR, на main и по workflow_dispatch; конкурентный устаревший run отменяется. Нет continue-on-error. JSON benchmark, translation report, experimental ZIP/build manifest/native exports — Actions artifacts. Они не публикуются как stable release.
