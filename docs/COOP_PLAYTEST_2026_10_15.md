# Первое совместное испытание: 15 октября 2026

Дата по Asia/Vladivostok. Отчёт заказчика и брата, а не запуск игры в среде разработчика.
Исходная main: `937318b7747eaf9048a3ebf35d30efd4d211ea0c`, после объединения PR #10.
Ветка исправлений: `fix/first-coop-gameplay-2026-10-15`, [PR #11](https://github.com/WestDed/TransportFever3-Multiplayer/pull/11), прямо в main. Автоматического merge нет.

## Что подтверждено пользователями

После исправления ресурсного каталога хост получил game ready. Porthole сначала не раздавал порт: раздел раздачи был пуст, клиент ожидал предложения портов. После запуска сервера, предложения и принятия TCP 28090 оба локальных TCP-теста прошли; пользователи затем сообщили об успешном соединении и совместной игре. TCP-тест сам по себе подтверждал только локальный listener, успешная игра подтверждена отдельно словами пользователей.

Для повторения: хост запускает MPFever/игру и создаёт сессию через игровое меню MPFever; предоставляет в Porthole TCP `127.0.0.1:28090`; клиент принимает порт и вручную подключается к своему `127.0.0.1` и локальному порту Porthole. Steam invitation нашего мода содержит внешний IP и может обходить этот туннель. На обеих машинах одинаковый полный пакет, игра/mods/DLC согласованы. Приватные ключи не передавать друг другу.

Это ограниченный положительный пользовательский результат startup/join. Длительный soak, восстановление владельца после настоящего save/restart, экономика и большая карта не приняты. Предыдущий отрицательный запуск сохранён в [отдельной записи](INCIDENT_2026_10_14_FIRST_GAME.md).

## Полная матрица замечаний

| ID | Сообщение пользователя | Разбор и состояние |
|---|---|---|
| PLAY-01 | Большая карта, reconnect около 50% | Уточнено пользователем: экран загрузки самой игры у брата, после передачи файла. Control reserve, separate load budget, snapshot-specific launcher heartbeat/ready реализованы. Причина engine stall без свежих logs не доказана |
| PLAY-02 | Нельзя строить вдоль чужой дороги, нет выдачи прав | Реализованы окно владельца, use/connect/edit, адресные grants, checks split/full-curve/delete/node/road-stop. Engine pre-apply требует игрового подтверждения |
| PLAY-03 | Платные дороги | Реализованы private/free/toll, один сбор за вход на участок, saved visits, debit→credit, stop при отсутствии денег, содержание за logical owner |
| PLAY-04 | Платная остановка недоступна | Owner resolver/cache/reload, free mode, адресный use, line validation, existing-route revoke и saved fee queue реализованы; реальное прибытие/payment NOT RETESTED |
| PLAY-05 | Одна городская subsidy доступна обоим | Saved exclusive UID claim реализован; simultaneous native apply/reward требует игры |
| PLAY-06 | Нет создания собственных subsidies | Реализованы intercompany delivery orders с формой создания, escrow, одним carrier, реальными cargo/passenger events, cancel/abandon/expiry/refund |
| PLAY-07 | Брат видит чужие notifications | GUI history/state/marker/popup prefilters по owner; свои и мировые остаются, обязательна проверка звуков в игре |
| PLAY-08 | Брат видит чужие floating income/expense | Native filter отдельного GUI world после replication, без изменения ACCOUNT/JOURNAL; alias guard. Реальный hook NOT RETESTED |

## Изменения PR #11

### Загрузка и соединение

`BoundedOutbox` оставляет snapshot traffic место для control traffic: до 512 KiB (на уменьшенном test budget — не более 1/8) и 96 из 128 frame slots. Общий лимит остаётся 8 MiB/128 frames, in-flight bytes учитываются, FIFO не меняется. Bulk producer ждёт свободного места, медленный клиент не блокирует других. Резерв конечен: остановившийся receiver всё ещё отключается по существующим лимитам; это не обещание работы на любой скорости.

Сохранение хоста допускает до 600 секунд; передача — 1800, согласовано с hard cap receiver. После постановки частей в очередь хост отдельно ждёт валидный `resync_received` от каждого клиента, затем начинает бюджет загрузки. Загрузка по умолчанию допускает 1800 секунд. На хосте допустим `MPFEVER_LOAD_TIMEOUT_SECONDS` от 60 до 7200; malformed/слишком большой параметр отвергается после команды pause. Idle socket не ограничивается временем загрузки; handshake, partial-frame и send deadlines не ослаблены.

Добавлены сообщения о стадиях, размере/числе частей, ожидании receipt/load, заполнении очереди и ошибке socket writer. Failed transfer/load сохраняет pause; нет автоматического resume повреждённого мира. Это локальные исправления существующего протокола 4, без нового транспортного канала или центрального scheduler.

### Остановки

UI использует тот же `R.ownerOf`, что simulation: поддерживается owner через construction станции при неудаче subconstruction lookup. Hook выбора станции можно установить позже при появлении module; попытки ограничены по частоте. Cache инвалидируется при смене shares/company map/local company; новый UI связывает свежий контекст вместо старого wrapper. Ограничения mutation permissions остаются; новая возможность сносить чужую инфраструктуру не добавляется. Существующая семантика `fee=0` означает «закрыто», а не бесплатный доступ; её миграция вынесена отдельно.

### Субсидии и карточки

Guard работает и до входа второй компании. Claim сохраняется в игровом состоянии перед обработчиком; повтор не доходит до функции, которая может начислить награду. При исключении внутри игрового обработчика claim остаётся fail closed; rollback частичных engine rewards не доказан. Одновременное принятие на границе native apply остаётся game gate общего порядка. Пользовательские заказы не подменяются городскими субсидиями.

Индекс карточек обходит именованные поля независимо от отсутствующих списков: свои active/completed остаются, чужие скрываются. Общие уведомления мира не фильтруются этим исправлением; финансовые popup не являются карточками субсидий.

## Доказательства автоматических проверок

Неизменённый production + первоначальные regressions: [run 38589711881](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38589711881), head `0c0a3b28`: Lua Windows/Linux дали 12 assertions failures (8 subsidies, 4 selection). Первоначальный queue fixture случайно оставлял 83 свободных байта, достаточных для двух controls; это было недостаточное насыщение теста, а не доказательство отсутствия сетевой ошибки.

Уточнённый baseline: [run 38590443119](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38590443119), head `2f570c09`. Bulk frame 141 байт при бюджете 1000 оставляет только 13: net8 Linux/Windows и actual net48 падают на `bulk queue reserves room for host status instead of disconnecting active download`. Lua воспроизводит также station-construction owner mismatch. Decoder и Windows build проходят. Failures не скрыты через skip/expectedFailure.

Исправленный code head `ebd3395f5e14b41d37e809a65aa0c6a4d4c36b84`: [run 38591306735](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38591306735), все семь jobs successful. 105 Python/Lua на обеих ОС (все 17 исходных lockstep scenarios), 336 C# checks в каждой из net8 Linux, net8 Windows, actual .NET Framework 4.8 Windows; 50 decoder checks, real TCP benchmark, полный Windows EXE/DLL/ZIP/license и hashes всех 15 зарегистрированных resources. Локально 105 Python/Lua PASS. Финальный documentation head и его CI фиксируются в PR.

Lua fixtures выполняют production modules/functions с injected engine facts, но не настоящие game scripts; C# console suite не исполняет WinForms lifecycle. MainForm с receipt/load изменениями собран целиком в Windows job, его engine orchestration ещё не испытана. В этом первоначальном пакете новые деньги/дороги/native addresses не вводились. Затем по запросу пользователя добавлены все оставшиеся механики: см. BUSINESS_MECHANICS; старый PASS не доказывает поздние изменения.

## Игровой повтор и недостающие данные

1. Одинаковый полный пакет PR #11 на обоих ПК, исходный save отдельно. Сначала малый save, затем тот же большой, где был отказ.
2. Записать game build/hash, размер `.sav`, время начала/окончания save, transfer, engine load; 50% уже уточнено: на экране загрузки самой игры у брата. Не запрашивать это уточнение повторно.
3. После проблемы предоставить свежие `launcher-*.log` обоих игроков и папки сессии из `%TEMP%\mpfever`. Нужны `mod.log`, `ui_mod.log`, `native.log`, `auto.log`, `in.log`, `out.log` и status, если есть. При падении — game stdout/crash log. Identity private files не запрашивать; raw logs с личными данными в GitHub не публиковать.
4. Для остановки: owner назначает тариф, клиент выбирает её в собственной линии, покупает/назначает транспорт, транспорт прибывает. Сверить owner, результат command, списание и зачисление на обеих машинах. Повторить после закрытия доступа и save/load; отсутствие лишнего списания отдельно.
5. Субсидия: один UID берёт A, B и повтор A не получают второй award. Затем другие UID, reverse roles, simultaneous click, complete/fail и save/load; rewards и deliveries должны принадлежать победителю. Карточки чужого завершённого заказа скрыты, общие новые предложения видны.
6. Для чужих notices/money: screenshot конкретного типа и времени, owner объекта, выбранная компания и журналы. Проверить свои уведомления и общие мировые события, чтобы privacy patch не скрывал их.

Игровое состояние исправлений PR #11: **NOT RETESTED**. Общее startup/join: **USER-REPORTED PASS**, большая карта и перечисленные механики: **USER-REPORTED FAIL / MISSING** до повторного подтверждения.

## Реализация полного списка по последнему запросу

Пользователь потребовал выполнить все механики до следующего игрового теста. В PR #11 добавлены pure saved business core, engine adapter, полноценное окно компаний/соглашений, GUI notification projection, native money filter и engine-load liveness; дорожные tariffs/ACL и собственные заказы больше не являются только планом. Подробные пользовательские действия, расчёты и ограничения — [BUSINESS_MECHANICS](BUSINESS_MECHANICS.md).

Первая полная CI попытка новых механик: [38683104340](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38683104340), head `3aa2b69df7f7b8032fcae36122d8608c5b11cc68`. Windows EXE/DLL/native filter и обе Lua jobs PASS; C# jobs FAILED на коллизии now в тестовом файле, затем исправлено без exemptions. После исправления локально 363 C# checks PASS. Финальные head/run/artifact фиксируются в PR, а не подменяются более ранним зелёным пакетом. Все новые game checks остаются NOT RETESTED.
