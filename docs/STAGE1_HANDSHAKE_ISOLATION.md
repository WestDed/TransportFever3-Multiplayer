# Граница принятия TCP-подключения

13.10.2026 (Asia/Vladivostok). [PR #6](https://github.com/WestDed/TransportFever3-Multiplayer/pull/6), `fix/handshake-isolation`, base `fix/offline-order-recovery` / PR #5. Основная ветка и авторский MPFever не изменяются. Игровых испытаний нет.

## Найденные ошибки

До исправления `Relay.Broadcast` выбирал все принятые **сокеты**, включая те, которые ещё не прислали допустимый `join`. Такой клиент мог читать общие сообщения сессии до проверки версии и reconnect credential. `Peers`/`PeerNames` уже отбирали `Joined`, но Broadcast этот фильтр не использовал.

Кроме того, внутри join хост устанавливал `p.Joined = true` и выходил из lock, после чего ставил `connection` в очередь. Параллельный Broadcast мог поставить `session` или другое сообщение раньше `connection`. `Client.Connect` ожидает подтверждение первым; это создавало причину случайного отказа на работающей сессии. Исправление прежнего синхронного Connect не устраняло эту гонку на стороне хоста.

Первое сообщение использовало общий лимит примерно 4 МиБ, хотя рукопожатие состоит только из версии и необязательного 64-символьного credential. До принятия клиента такой объём не нужен.

## Изменения

- Broadcast выбирает только `Joined`. Адресный `SendBulk` также отказывает незавершённым подключениям; обходная отправка допустима только для собственного `connection` и best-effort `reject` в обработчике рукопожатия.
- Под тем же lock списка peers сначала ставится `connection` в независимую bounded очередь, затем публикуется `Joined`. FIFO writer обеспечивает порядок байтов; ожидания socket write под этим lock нет. Замена старого сокета с тем же credential происходит после успешного enqueue.
- Первый входящий frame ограничен **4096 UTF-8 байтами до LF** на хосте и клиенте. Ограничение проверяется по мере накопления, до Lua parsing и даже без завершающего LF. Остальные сообщения сохраняют прежний общий предел.
- `BoundedLineReader.ReadLine` принимает меньший лимит конкретного frame. Тот же reader используется после join, поэтому уже прочитанное следующее сообщение не теряется. Неизвестные/числовые join keys отвергаются; поля protocol/resume и wire version 3 сохранены.

Небольшие source и regression commits отделены. Новая реализация транспорта, центральный replay и новые игровые hooks в этом PR отсутствуют.

## Ошибка ожидания Connect, обнаруженная CI

Первый run 38318050228 прошёл все шесть jobs. На следующем run 38318246921 тот же C# код упал на Ubuntu: существующий тест forged reconnect получил `ObjectDisposedException` с `SafeWaitHandle` в `Client.Connect` вместо ожидаемого отказа credential. Новые handshake tests до этого места прошли. Ошибка реальная для используемого net8 runtime; это не отсутствие зависимости и не основание ослаблять тест.

Connect использовал BeginConnect и `using(connect.AsyncWaitHandle)`. [Task-to-APM adapter .NET](https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Threading/Tasks/TaskToAsyncResult.cs) возвращает handle внутреннего Task; ручной Dispose этого handle нежелателен при повторном использовании завершённой Task. Связь с кэшированным Task — объяснение по исходникам, не отдельная трассировка runtime данного runner.

Путь заменён на `TcpClient.ConnectAsync`, bounded `Task.WaitAny` и `GetAwaiter().GetResult()`. Внешний wait handle больше не закрывается нашим кодом; исходная SocketException сохраняется. На timeout внешний catch закрывает сокет, поздняя faulted Task наблюдается continuation. Добавлен настоящий отказ подключения к зарезервированному, но не слушающему порту: ожидаются исходная SocketException и отсутствие accepted identity/events. Обязательная net48 сборка проверяет применимость выбранного API production-лаунчеру.

## Проверки

`tests/offline/HandshakeTests.cs` исполняет реальные Relay/Client/reader из production исходников. Проверяются exact cap и UTF-8 byte count, ранний отказ без LF, сохранение следующего большого buffered frame, отказ Client от oversized host response, отсутствие сообщений у pending socket, исправный broadcast joined клиенту, join+chat в одном TCP write, malformed/oversized join и освобождение reserved slot. Дополнительно 32 reconnect цикла идут при параллельном session broadcast; подтверждение должно быть первым.

Локально 67 существующих Python/Lua tests проходят после установки `tests/requirements-offline.txt`. Первая попытка в восстановленной scratch-среде остановилась на `ModuleNotFoundError: lupa`: это отсутствующая test dependency, а не ошибка мода. В workflow зависимость уже устанавливается явно; exemptions не добавлялись.

Результат C#/Windows сборки фиксируется по **точному head и run в PR #6**. Workflow по-прежнему проваливается при любом неожиданном failure, проверяет Ubuntu/Windows TCP/file suite, C# LuaLit, Python/Lua и полный production net48 EXE/x64 DLL/package/license. PASS предыдущего PR не является результатом нового кода.

## Предел результата

Принятый join означает проверенный формат/версию и, при reconnect, session credential. Он **не подтверждает личность нового игрока**: initial admission, TLS, persistent company ownership, Steam proof и compatibility fingerprints остаются задачами. Наблюдатель открытого TCP по-прежнему может получить bearer credential. Эту изоляцию нельзя называть полноценной authentication или разрешением публичного интернет-релиза.

TCP/файловые тесты не запускают TF3, не проверяют движок/сохранения/экономику и не закрывают весь этап 1. Main/merge остаются под управлением заказчика.
