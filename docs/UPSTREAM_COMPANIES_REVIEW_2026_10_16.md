# Отдельные компании: предупреждение, upstream и наши изменения

Проверено 16.10.2026 (Asia/Vladivostok). Это исследование исходников и публичных отчётов, не игровой прогон. Production-код в этом PR не изменён. Отчёт намеренно различает воспроизведённый компонентный дефект, отсутствующее upstream-исправление и ещё не принятый игровой результат.

## Короткий вывод

Предупреждение об экспериментальности обоснованно. Наши новые механики не устраняют все ограничения исходного MPFever. Кроме того, после нашей исходной базы 0.3.6 автор выпустил 0.3.7–0.3.11 с важными исправлениями строительства, маршрутов, покупок и загрузки. Ряд этих исправлений отсутствует даже в нашем PR #12. Два конкретных дефекта подтверждены ниже изолированными вызовами production Lua.

Улучшения отдельных компаний и исправления общего механизма синхронизации — разные части работы. Успешные 175 тестов PR #12 не являются доказательством устранения перечисленных здесь дефектов: прежний набор не содержит соответствующих сценариев/игровых данных. Нельзя объявить наш пакет исправленным только потому, что исправление выпущено upstream.

## Какие версии сравнивались

| Снимок | Точная ревизия | Состояние при проверке |
|---|---|---|
| Наша main | `937318b7747eaf9048a3ebf35d30efd4d211ea0c` | PR #10 объединён; #11–12 ещё открыты |
| Наш PR #11 | `de3dca1e3a596e2e29536d8ff3004c7a5cd7788a` | Механики дорог, остановок, заказов, privacy и loading liveness; не объединён |
| Наш PR #12 | `ad6b6f979fef5923c8d44d88ef2917fa25a11fce` | Включает #11, добавляет ручной station fee; не объединён |
| Исходная база нашей разработки | `0c4a6d35bb5fd22f2ef851d8089e4c6f2cde6773` | MPFever 0.3.6; зафиксирована первоначальным аудитом |
| Актуальная upstream main | `171d35c19574f1b8fdf1bc8f174755f30ebb54bc` | README/CHANGELOG 0.3.11-experimental; последний релиз 0.3.11 опубликован 15.10.2026 08:01:57 UTC |

Наше название **0.3.7-experimental** не означает равенства авторскому **0.3.7**: это разные ветки, protocol и набор исправлений. Для следующих сборок нужен различимый идентификатор нашей ветки и commit в пакете/диагностике. До того ориентироваться на SHA/manifest, а не только на номер окна.

## Откуда текст предупреждения

В [нашем menu на PR #12](https://github.com/WestDed/TransportFever3-Multiplayer/blob/ad6b6f979fef5923c8d44d88ef2917fa25a11fce/mod/mpfever_1/content/mpfever_menu.lua#L251) условие `companies:old() == "separate"` всегда добавляет три строки предупреждения. Это статический текст автора, а не диагностика вашего ПК/сохранения и не результат неудачного соединения. Он остаётся и в [upstream menu](https://github.com/dkdknight/MPFever/blob/171d35c19574f1b8fdf1bc8f174755f30ebb54bc/mod/mpfever_1/content/mpfever_menu.lua).

Основные первичные источники:

- [Upstream README, закреплённая ревизия](https://github.com/dkdknight/MPFever/blob/171d35c19574f1b8fdf1bc8f174755f30ebb54bc/docs/README.txt): KNOWN LIMITATIONS и WHAT WORKS.
- [Upstream CHANGELOG, та же ревизия](https://github.com/dkdknight/MPFever/blob/171d35c19574f1b8fdf1bc8f174755f30ebb54bc/docs/CHANGELOG.txt): исправления 0.3.7–0.3.11.
- [Upstream RESEARCH](https://github.com/dkdknight/MPFever/blob/171d35c19574f1b8fdf1bc8f174755f30ebb54bc/docs/RESEARCH.md): single-company scripts, UI/simulation split, остающиеся проверки.
- [Релизы](https://github.com/dkdknight/MPFever/releases), в частности [0.3.11](https://github.com/dkdknight/MPFever/releases/tag/0.3.11).
- [Страница автора Nexus](https://www.nexusmods.com/transportfever3/mods/4): при проверке всё ещё показывала 0.3.7, поэтому не использована как источник последней версии. Старые объявления Steam/Reddit/ModDB про единственную общую компанию относятся к 0.1 и не описывают текущий код.

GitHub issue со статусом closed означает закрытый отчёт. Где ниже сказано «исправлено upstream», это подтверждено CHANGELOG и различием production-кода, но не нашим запуском игры. Даже у последнего upstream остаются новые отчёты.

## Что с независимостью компаний

| Возможность / проблема | Что в исходном MPFever | Что у нас после PR #12 | Что остаётся |
|---|---|---|---|
| Уровень, опыт, часть разрешений | Компании читают progression компании сохранения | `coPatchProgression` по-прежнему возвращает `R.CO.canon`; это обход single-player assumptions | **Не исправлено:** независимый прогресс отсутствует. Отдельные разрешения на строительство/unique buildings не равны независимому rank/XP |
| Действия окна компании | Реклама, озеленение, поиск промышленности работают для компании хоста; чужие запросы игнорируются | `executeAction` по-прежнему отказывает `Company`/`Companies` event другой компании, чтобы не списать/применить действие хосту | **Не исправлено.** Нужен adapter каждого действия с проверкой владельца, цены и результата. Просто убрать guard нельзя |
| Депо других игроков | Депо нельзя делить между компаниями | Наши road/station ACL не добавляют shared depot policy | **Отсутствует.** Права на остановку не разрешают покупку/обслуживание в чужом депо |
| Денежные переводы | Свободных переводов между компаниями нет | #11 добавляет конкретные road/station payments и escrow заказа | **Частично:** расчёты за услугу есть; произвольной кнопки перевода денег нет. Debit/credit не атомарны; submitted после reload остаётся uncertain |
| Городская субсидия | Single-company assumptions и прежний повторный claim | #11 сохраняет одного владельца claim, отказывает повторному/чужому приёму | Реализовано в коде; одновременный accept и настоящая выдача награды требуют игры |
| Собственные заказы | Такого интерфейса/правил в нашей исходной базе не было | #11: create, резерв награды, один carrier, cargo/passenger delivery, cancel/expiry/refund | Реализовано в коде; фактические engine delivery events/проводки ещё NOT RETESTED |
| Чужая инфраструктура | Station fee был, полного road ACL не было | #11: use/connect/edit, private/free/toll; #12: ручная сумма station fee | Реализовано в коде; все native builders должны действительно соблюдать запрет до apply. Наличие controls этого не доказывает |
| Чужие уведомления/денежные надписи | Авторские UI обходы, проблема замечена нашими игроками | #11: GUI projection и native destination filter с alias guard | Нужна проверка настоящих recipes/sounds/hooks. Это скрытие UI, не секретность состояния мира от клиента |
| Владелец/финансы после restart | Реестр компаний и accounts | Наша main уже содержит persistent key ownership, saved company id/loan state; обычный баланс хранит game ACCOUNT | **Не отсутствует.** Обязателен настоящий save/полный host restart/rejoin; отдельно проверить неопределённую проводку. Это два разных вопроса |
| Кредиты | Отдельный loan adapter | Repay уже берёт условия сохранённого loan, но Obtain всё ещё использует `obtained.amount` из event | Неполная проверка серверного предложения и условий Obtain — известный source-level риск, не утверждение о воспроизведённом игровом взломе |

Доказательства в нашем [bridge PR #12](https://github.com/WestDed/TransportFever3-Multiplayer/blob/ad6b6f979fef5923c8d44d88ef2917fa25a11fce/mod/mpfever_1/content/mpfever_bridge.script.lua): `executeAction`, `coLoanEvent`, `coPatchProgression`. Реализованные правила и пределы — [BUSINESS_MECHANICS в PR #12](https://github.com/WestDed/TransportFever3-Multiplayer/blob/ad6b6f979fef5923c8d44d88ef2917fa25a11fce/docs/BUSINESS_MECHANICS.md), [RISKS там же](https://github.com/WestDed/TransportFever3-Multiplayer/blob/ad6b6f979fef5923c8d44d88ef2917fa25a11fce/RISKS.md). Документы PR #11–12 ещё не лежат в main; ссылка всегда указывает нужную ревизию.

## Какие upstream-исправления ещё нужно переносить

Это дефекты общего multiplayer, которые затрагивают и separate companies. Сравнены реальные `common`, `bridge`, `refs`, `ui`, native и `MainForm`, а не только номера версий. «Не перенесено» означает отсутствие конкретного исправления в исследованном коде; игровой симптом на нашем пакете без запуска не объявляется повторённым.

| Приоритет / область | Отчёт и авторское исправление | Наш код PR #12 / предел вывода |
|---|---|---|
| P0: снос уличной станции | [#15](https://github.com/dkdknight/MPFever/issues/15), 0.3.7: вместе с подъездной улицей удалять станцию, иначе engine assert у другого игрока | Новая версия removalPlan отсутствует; наши station permissions не исправляют сам replay сноса |
| P0: рельсы, сигналы, пересечения | [#16](https://github.com/dkdknight/MPFever/issues/16), [#26](https://github.com/dkdknight/MPFever/issues/26), [#28](https://github.com/dkdknight/MPFever/issues/28), 0.3.7–0.3.8: сохранять вид/длину/оформление сегмента, node config и удаление старых сигналов | Соответствующих обновлений replayStop/replayStreet/node-config пути нет. Риск тоннеля/моста, collision и reload остаётся |
| P0: порядок стройки и reload | [#17](https://github.com/dkdknight/MPFever/issues/17), [#18](https://github.com/dkdknight/MPFever/issues/18), [#19](https://github.com/dkdknight/MPFever/issues/19), 0.3.7 | У нас count-based `release`, старое замещение `g_preSeen[0]` при переполнении и нет нового replayBusy gate. Нужен согласованный перенос Lua/native/launcher, а не правка одного timeout |
| P0: стоимость/город/номера entities после стройки | [#20](https://github.com/dkdknight/MPFever/issues/20), 0.3.7; CHANGELOG 0.3.9: context flags и tool replacement maps, обработка перемещённых town buildings | Эти новые пути отсутствуют. Исправление баланса хостом не восстанавливает геометрию, town ownership или порядок entity ids |
| P0: покупки при остановленном времени | [#35](https://github.com/dkdknight/MPFever/issues/35), 0.3.10: одна покупка на game step, сохранённый `st.buyAt` | У нас `bought=false` сбрасывается при каждом `simApplyDue`, а не при новом шаге. **Компонентно подтверждено ниже** |
| P0: купить и сразу назначить линию на паузе | [#36](https://github.com/dkdknight/MPFever/issues/36), 0.3.10: ждать callback создания перед зависимыми командами | `runPendingPaused` ещё обходит все готовые actions без нового ожидания create callback. Наш offline CommandJournal не подключён к scheduler и не устраняет это |
| P1: host restart, save принят, но не загружается | [#29](https://github.com/dkdknight/MPFever/issues/29), 0.3.8–0.3.9: уникальный локальный номер каждой команды загрузки | У нас `int resyncId` начинается заново, клиент пишет `menuLoadSeq="r"+id`, auto сравнивает с persistent `menu_loaded.txt`. SHA/transfer receipt из наших fixes не меняет этот GUI load token. Это отдельный риск от большого save и сетевого timeout |
| P1: числа / локаль | [#30](https://github.com/dkdknight/MPFever/issues/30), 0.3.8: `%.0f` вместо 32-bit `%d`, фиксированные control bytes вместо locale `%c` | У нас `C.ser`/hash ещё используют старые операции. Отчёт автора сам source-level; на нашем Linux Lua нельзя выдавать большой integer roundtrip за проверку game Lua/Windows ABI |
| P1: путевые точки маршрута | [#27](https://github.com/dkdknight/MPFever/issues/27), 0.3.8: typed Waypoint, перевод track refs и hash | У нас `LineStop.waypoints=false`. **Компонентно подтверждена потеря**; отдельная настройка остановок/тарифов этого не касается |
| P1: карта маршрутов клиента | CHANGELOG 0.3.7: LineViewer запоминает компанию до её определения; новый LinesDetour | У нас нет `RVA_LINES=0x7f5c60`/LinesDetour, остался прежний UI operand patch. Перенос native hook требует проверки game build/signature/ABI; не считать его безопасным по одному совпавшему RVA |
| P1: большие станции / периодические подвисания | [#25](https://github.com/dkdknight/MPFever/issues/25), [#32](https://github.com/dkdknight/MPFever/issues/32), 0.3.8: индекс refs на action, ротация тяжёлых частей hash | У нас остаётся поиск без нового индекса; `R.hashSkip` читается в refs, но bridge его не задаёт. Load heartbeat не ускоряет эти участки. Gigantomaniac не профилирован |
| P1: копирование транспорта / callback окна | [#21](https://github.com/dkdknight/MPFever/issues/21), [#22](https://github.com/dkdknight/MPFever/issues/22), 0.3.7 | В marshal остаются пробные обращения к userdata (включая `v[16]`), новые vector/type caches и callback shape не перенесены. Дампы/фризы нельзя назвать воспроизведёнными без game userdata |
| P1: terrain tool одновременно со стройкой | [PR #41](https://github.com/dkdknight/MPFever/pull/41), 0.3.11: убрать взаимное ожидание leader и held build | У нас старый terrain lead путь. Добавление road ACL не исправляет эту barrier-проблему |
| P2: удалённые уведомления возвращаются после resync | [PR #42](https://github.com/dkdknight/MPFever/pull/42), 0.3.11: session-local dismissed/filter state | Наше скрытие чужих сообщений решает другую задачу. Локальные dismissed/filter persistence не перенесены |
| P2: интерфейс старта/скорость/паузы | [#24](https://github.com/dkdknight/MPFever/issues/24), [#34](https://github.com/dkdknight/MPFever/issues/34), [#37](https://github.com/dkdknight/MPFever/issues/37), PR #38–40, 0.3.8–0.3.11 | Ответ кнопке скорости, сокращение stopped rounds и закрытие release-notes окна ещё не перенесены. Нового авторского lobby тоже нет; это функция, а не доказательство поломанного прямого join |

## Что не исправлено даже последним upstream

1. [Открытый PR #43](https://github.com/dkdknight/MPFever/pull/43): долгий capture принятой стройки ошибочно объявляется отказом через 150 UI frames. Другой ПК перестаёт ждать и строит позднее. В отчёте — реальная городская улица, разные costs и число удалённых домов; patch ещё не объединён. И у нашей ветки, и у upstream 0.3.11 остаётся frame-only `expireShipIds`. Это не тот же случай, что 50% на загрузочном экране брата.
2. [Открытый PR #44](https://github.com/dkdknight/MPFever/pull/44): экспериментальный builder lead для быстрого строительства, зависит от #43, выключен по умолчанию. Автор сообщает и об оставшихся money corrections/городских desync в ручных сессиях с переключателем и без него. Поэтому даже «перейти на самый новый upstream» не означает получить доказанно стабильный multiplayer. Его быстрый режим нельзя включать как готовое исправление задержек.
3. В README/RESEARCH остаются single-company progression/actions, shared depots, ground paint, длительные игры, 3+ игрока и зависимость native hooks от game build. Tutorial не поддержан: отдельная проверка миссий приводит к разным состояниям. Никакая наша новая таблица заказов это не меняет.

## Два проверенных без игры примера

Среда: Python 3.12, `lupa==2.6`, Lua 5.2; dependency из `tests/requirements-offline.txt`. Источники — точные ревизии таблицы выше. Вызовы read-only, без правок production.

**Waypoint.** Загружен наш настоящий `mpfever_common.lua`; `api.type.Line.Stop.new` заменён конструктором пустой таблицы. Вызов `C.rebuild("LineStop", {waypoints={{trackEntity=123,param=0.5}}})` возвращает объект без `waypoints`. Вывод: `output waypoints retained: False`. Это доказывает отбрасывание поля нашим schema/fill, не поведение native pathfinding.

**Покупки.** Из настоящего bridge извлечена целиком `simApplyDue(state)`. Две ожидающие `makeVehicleBuyCmd`, `at=100`, два вызова функции при неизменном `gameTime()=100`. Подставлены clock, command executor, пустые engine dependencies; сами покупки в игре не выполняются. Наш код передал executor **2** покупки; upstream 0.3.11 — **1**. Проверяется scheduling, не создание/нумерация реальных машин.

Это диагностическое воспроизведение старых ошибок, а не зелёные регрессии исправленного проекта. При исправлении добавить оба сценария в постоянный suite и доказать red→green. Нельзя маскировать их expectedFailure/исключениями ради зелёного CI.

## Предлагаемый порядок работы

1. **Сначала закрыть накопившиеся P0/P1 upstream gaps небольшими PR.** Для каждого: read pinned change, адаптация к нашим guards/schema/snapshots, red regression → fix → весь suite → Windows build. Начать с формата чисел/waypoints и scheduling покупок, затем согласованно перенести removal/сигналы/context/replacement maps/replay ordering и native release id. Уникальный GUI load token выделить отдельно: он важен для owner/host restart сценария. Не обещать, что все эти изменения независимы.
2. **Сохранить наши улучшения при переносе.** В актуальном upstream `C.deser` всё ещё делает `load("return "..s,...)`. Нельзя заменить наш common целиком и вернуть eval. Также сохранить bounded IPC/TCP, RSA ownership/roster, schema, snapshot SHA/atomic commit/receipt/liveness, saved loan Repay terms, business/ACL и GUI filter-after-copy. Старый upstream ReplDetour очищает simulation money positions перед копированием; наша destination-only privacy логика должна пережить перенос native fixes.
3. **Добавить targeted cases вместо повторения прежних 175 тестов без новых сценариев.** Waypoint/typed refs, повторный sim update при одном step, paused purchase→line callback, repeated resync/load token, slow capture cancellation; для native — совместная сборка DLL/Lua controls и проверка signatures. Индексирование refs не должно менять результат разрешения. Schema должна явно принимать новые proposal/map/waypoint поля без ослабления owner validation.
4. **Затем устранить ограничения компаний.** Company actions по отдельности; Obtain по server offer/terms; независимые XP/rank/permits; при необходимости shared depot и обычные переводы. Платёжную очередь не называть атомарной, пока нет reconciliation с engine journal.
5. **Игровая приёмка на двух ПК:** сохранить/build/hash/моды, owner+balances до/после host restart, live custom fee, escrow/delivery; затем все указанные replay cases на pause/x1/x4. Отдельно большая карта: transfer/load, station edits, hash p50/p95, capture duration и RSS. 3 игрока/длительный soak — следующий gate, не вывод из двух клиентов.

Предупреждение пока оставляем. Позже его стоит сделать конкретнее и связать с актуальным списком ограничений, но удаление предупреждения не является исправлением режима. На этом этапе выполнен аудит и предложена последовательность; перенос перечисленных новых исправлений ещё не выполнен и не должен фигурировать как готовый.
