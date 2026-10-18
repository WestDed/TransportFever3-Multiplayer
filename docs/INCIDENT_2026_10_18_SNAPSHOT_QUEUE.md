# Обрыв передачи большого сохранения — 18.10.2026

## Что подтверждено пользовательскими логами

Изучены семь launcher logs хоста и один клиента, а также native audit. Последний запуск хоста сообщает game build 40420 и `game ready`. Клиент проходит вход в сессию и начинает получение сохранения. В восьми попытках размеры host snapshot возрастают от 171531041 до 174497012 bytes; ни одна из них не содержит подтверждённого успешного получения/загрузки клиента. В пяти попытках непосредственно перед отключением хост пишет:

```text
Outgoing queue full or closed for <client>: session
Outgoing queue full or closed for <client>: peerclock
Outgoing queue full or closed for <client>: mpstatus
Snapshot transfer cancelled or peer too slow
Resynchronisation failed; all games remain paused.
```

Остальные три разрыва не имеют достаточно подробной причины в этих logs. Клиентский log последней попытки совпадает по snapshot id 8, размеру 174497012 bytes и 666 parts. Часы ПК отличаются примерно на 7 часов, поэтому абсолютные HH:mm:ss нельзя сравнивать без поправки. Клиент пишет об успешном соединении с локальным портом Porthole и затем о потере связи. Соединение/принятие владельца прошли; `verified and committed` отсутствует, до engine load эта попытка не дошла.

Это не доказанная причина прежнего отказа на 50% **игровой** загрузки и не повтор GameScriptState crash. Raw logs, public addresses, personal filesystem paths, owner fingerprints и world identifiers в репозиторий не копируются. Native audit содержит счётчики перехватов и не устанавливает причину сетевого обрыва.

## Дефект конечного резерва

`BoundedOutbox` уже резервировал bytes/frame slots для control traffic, но продолжал хранить каждое периодическое сообщение. Пока большой snapshot занимает медленный writer, `peerclock`, `session` и `mpstatus` продолжают поступать. Конечный резерв заполняется, `Relay.Send` получает false и сам закрывает peer. `WSACancelBlockingCall` после такого Close является последствием закрытия сокета; он сам по себе не доказывает ошибку настройки Porthole. Другие сетевые причины в остальных попытках не исключены.

Начальный тестовый CI воспроизводит переполнение на неизменённом production runtime main c06a075 во всех трёх C# средах. Уточнённая regression использует неизменное состояние паузы и дополнительно требует сохранения каждого реального перехода session. Точные baseline/final heads/runs и counts фиксируются в PR #17. Это исполнение production C# writer с управляемой блокировкой stream, не игровой запуск.

## Исправление .4

- Общий предел остаётся **8 MiB/128 frames**, включая кадр в записи. Bulk reserve, 60 секунд socket write, 120 секунд ожидания bulk queue и существующие save/transfer/load budgets не увеличиваются.
- Pending `peerclock` объединяются отдельно для каждой пары **sender + payload.name**. Использование только From ошибочно: все пересланные часы имеют host в этом поле.
- Pending `mpstatus` заменяется текущим полным UI snapshot. Новый кадр переносится в конец своего сегмента; новые часы не подменяют старый слот впереди ранее поступивших сообщений.
- **Каждое новое состояние session — граница порядка.** Только байт-в-байт одинаковый pending session heartbeat пропускается, сохраняя раннюю позицию уже поставленного сообщения. Pause/resume/speed transitions не теряются и не оказываются после более поздних часов.
- Любой другой кадр — граница: action, money operation, chat, peerleft, hash, resync_begin, **каждая** часть сохранения, resync_load и неизвестные types. Индекс заменяемых статусов очищается; статусы по другую сторону этих кадров не объединяются.
- Кадр in-flight неизменяем. При увеличении replacement проверяется фактический byte/frame budget; неуспешная замена сохраняет прежний кадр. Closed queue по-прежнему отказывает. Размер индекса ограничен числом pending frames.
- Логи запуска сообщают package/protocol/UTC; ошибка очереди дополнена frames/bytes/coalesced/closed. Старый общий текст ошибки не позволял отличить исчерпание reserve от уже закрытого writer.

Это локальная правка очереди и тестирования. Личный progression из PR #16, native offsets/hooks, игровые деньги/заказы/ACL и схема сохранения остаются в основе актуальной main. Пакет **0.3.7-westded.4-experimental**, FileVersion **0.3.7.4**, protocol **9**; полный набор обоим. Номер протокола отделяет новый пакет от старых; формат сообщений не переписывается.

## Проверки

`OutboxTests.cs` выполняет настоящий writer: 3000 state updates при неподвижном socket writer; место для ordered action/load; сохранность частей файла; clocks нескольких игроков/отправителей; replacement tail order; границы action/begin/part/load/peerleft/hash/money; реальные session transitions; неизменность in-flight; общий frame cap при разных state keys; byte cap и отказ увеличенной замены; closed queue. Нельзя объявлять такой тест испытанием Porthole или TF3.

TCP benchmark передаёт generated **32 MiB × 1/4/8 peers** через настоящие Relay/Client/SnapshotSource/SnapshotReceiver с SHA-256/commit. Теперь параллельно каждые 25 ms идут session/UI status и отдельные player clocks. Поле `periodicStateTraffic=true` отличает новый стенд от прежних скоростей без этого потока. Это loopback; latency/loss/реальная пропускная способность Porthole не моделируются. Python/Lua/C++ suite остаётся 289; точные итоговые C# counts и Windows package verification — в PR.

## Приёмка у заказчика

1. Исходный save отдельно; закрыть игру/лаунчер обоих ПК и установить одинаковый полный .4 пакет. Продолжить с прежними собственными identity profiles.
2. Сохранить рабочую схему Porthole: TCP port предоставлен/принят, клиент вручную подключается к показанному локальному endpoint. Новая настройка роутера не требуется для повторения уже принятого соединения.
3. Повторить вход на той же большой карте. Успех передачи — клиент пишет `host snapshot verified and committed` для нужного id/размера, хост получает receipt, затем клиент доходит до game-ready и общая сессия продолжается. Начало получения файла само по себе не PASS.
4. Проверить отдельные компании, паузу/скорость, последовательность действий, полный save/host restart/reconnect; убедиться, что деньги/owner/XP сохранились.
5. Если снова обрыв: свежие полные launcher logs обоих ПК из этого запуска (с header version/UTC), screen стадии загрузки и session mod/native/game logs при необходимости. Не удалять временные logs до сбора. Не отправлять private identity keys.

Неограниченно медленный/остановившийся канал по-прежнему завершится bounded отказом. Устойчивость через реальный Porthole и engine load имеет статус **NOT RETESTED IN GAME** до такого повтора.
