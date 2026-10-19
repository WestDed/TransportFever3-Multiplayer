# Риски и текущие ограничения

20.10, .7: [новые логи подтверждают резкий провал скорости, но не определяют виновный слой](docs/TRANSFER_STALLS_2026_10_20.md). Реальный net48 loopback на 192,5 МБ провала не воспроизводит; это не тест Porthole/пользовательского диска. Cancel/закрытие теперь сохраняют checkpoint, авария ОС может потребовать повтора последних частей. Вложенные таймеры не складывать как CPU-время.

19.10, .6: [границы ускорения подключения](docs/FASTER_CONNECTIONS.md). Нет замера на ПК заказчика; Porthole/диск/движок могут оставаться ограничением. Partial применим только к тому же SHA/размеру/parts, новый текущий мир может требовать полный снимок. До запуска смена состава сбрасывает ready; после начала загрузки нужен свежий recovery. Максимум partial 1 GiB на профиль и completed cache 2 GiB; старые профили не удаляются глобальным фоновым сборщиком. Игровая приёмка NOT TESTED.

19.10: [новые результаты .4 и ограничения .5](docs/LOBBY_AND_SNAPSHOT_CACHE.md). Повторный download 21:33 при ~189 MB против 1:47/~169 MB; причина падения темпа не установлена. Подтверждены отказы schema для speed=3, исправлены в .5. Лобби readiness не равно engine-ready. Кеш требует точного SHA и дополнительного диска, не проверяет все сторонние моды/версию TF3. Команды во время recovery и script:loan/nodeConfigs drift не объявлены устранёнными.

18.10: [подтверждённый обрыв передачи большого snapshot](docs/INCIDENT_2026_10_18_SNAPSHOT_QUEUE.md). Конечный reserve для control traffic не выдерживал непрерывных session/peerclock/mpstatus. .4 ограничивает повторные статусы без потери ordered traffic. Это не исправление качества Porthole/WAN и не проверка engine load; send/transfer limits остаются конечными. Нагрузка теперь включает concurrent periodic status traffic.

18.10.2026: **подтверждён игровой вылет .2** в progression registry при старте карты. Сохранённый GameScriptState читался из другого callback; этот доступ удалён в .3. [Инцидент, граница доказательства и приёмка](docs/INCIDENT_2026_10_18_MAP_LOAD.md). Native assertion не ловится Lua pcall. Fixtures теперь моделируют истечение callback handle, но безопасный ECS reader и запуск карты должны пройти игровой повтор. Прежний зелёный CI не закрывал этот риск.

16.10.2026, PR #15: [риски личного развития и engine gates](docs/COMPANY_PROGRESSION.md). XP теперь связан с собственными перевозками; баланс и сверка события цены с окончательным journal ещё не измерены. Городские эффекты остаются общими; личный уровень/подсчёт зданий не доказывают правильное применение всех native ограничений/улучшений.

16.10: [offline fixes](docs/OFFLINE_UPSTREAM_FIXES.md) устраняют ряд data/queue/UI дефектов; оставленные game-only риски перечислены там отдельно. Rotated hash проверяет каждую тяжёлую группу раз в три checkpoints, сохраняет прежний pending mismatch и требует двух собственных наблюдений для hard difference. Это снижение числа операций, не измеренная скорость на большой карте. Native geometry, hold/capture deadlines и независимая engine economy этой работой не подтверждены.

Результаты offline tests не подтверждают работу Transport Fever 3. В нашей среде игровые бинарники/saves не использовались; заказчик и брат сообщили о первом совместном запуске и обнаруженных отказах. Исправленные memory/code-execution risks и изоляция незавершённых TCP-подключений описаны в STAGE1 docs; ниже остающиеся ограничения.

| Риск | Наблюдение по исходникам/стенду | Что нужно для снятия |
|---|---|---|
| Разные симуляции мира | Каждый ПК рассчитывает мир; не полный authoritative world server | Same build/mods, game census, action-free drift и длительность |
| Premature native apply | Автор мог применить стройку до server verdict | Hold/release/replay автору и engine gate ADR 0001 |
| Save/load boundary | Нет production epoch/frontier transaction и drain всех callbacks | Привязать snapshot к applied frontier; crash/reconnect игровые сценарии |
| Личный progression | Новый adapter #15 заменяет общий reader после миграции | Совпадение game modules/payloads, реальные XP/цены/permits/save; fallback не считается PASS |
| Финансы | CorrectMoney, частичный loan/contract routing; Obtain ещё принимает недоказанные terms | Штатный loan_util/API, ledger history/owners/insufficient funds в игре |
| Station billing | Per-stop index не является stable visit ID | Loop/reroute/stop reorder/reload, одна оплата/нулевая сумма transfers |
| Identity | RSA key possession и saved owner реализованы; это не Steam authentication/initial invitation | Host admission, TLS/server trust, реальные save/load/migration и backup key |
| Invalid owner roster | PR #9 проверяет все rows/claims до commit и блокирует новые права до корректного report; runtime/Lua регрессии пройдены | Реальный engine roster/load timing; исходные данные game registry не заменяются и уже выполненные native actions не откатываются этим policy |
| Открытая сеть | TCP не шифруется; активный MITM может изменять последующие сообщения. Session token без private key теперь недостаточен | Выбор/проверка защищённого транспорта и доверия хосту до публичного использования |
| Недостаточная schema | Whitelist/shape/co policy не знают всю engine Proposal/ref/price структуру | Actual API и native ownership matrix; новые mod events явными schemas |
| Hooks/версии | Native addresses/ABI определены авторами для отдельных builds | Game hash/build gate, byte checks и каждый hook на реальной версии |
| Version ≠ compatibility | Protocol 5 не проверяет все EXE/DLL/mod/game hashes | Fingerprint одинакового пакета и полного engine/mod set |
| Память | 8 x 32 MiB loopback: RSS до ~693 MiB на одном hosted прогоне | Budget production/net48/игры, sustained profiling; меньше копий не равно меньше RSS |
| Размер/скорость снимка | 1 GiB/4096 parts; PR #11: bounded control reserve, verified receipt и load по умолчанию 1800 секунд; idle/partial/socket limits остаются | Параметры по настоящим saves/WAN; не обещать Gigantomaniac по этим caps |
| Disk durability | Flush(true)/atomic replace проверены обычными файлами | Power loss/filesystem/game save closure; дисковая ошибка сохраняет old save и pause |
| Journal foundation | Durable input order, но эффекты+frontier не атомарны в игре; компонент не активирован | Engine adapter/async callbacks/quorum по реальному состоянию |
| Lifecycle | Generation checks уменьшают stale callbacks, но не заменяют atomic session fence | Быстрая смена сессии/menu/late callbacks и cancellation в игре |
| Host failure | Нет host migration; постоянный key восстанавливает owner, session credentials/epoch новые | Реальный host/save restart, recovery snapshot/journal; backup host/client private profiles |

Hash снимка обнаруживает повреждение байтов, а не неправильную стройку/грузопотоки/финансовые проводки. Совпадающий hash ошибочного мира не является успехом. Реальный negative test проверяет код компонентов, но injected owner/loan facts не являются измерением двигателя.

Upstream bugs #15–22 и первоначальные доказательства/гипотезы доступны в [историческом аудите](https://github.com/WestDed/TransportFever3-Multiplayer/blob/6611e8f63e29e04aeb978bddd17877b461894edf/RISKS.md). Стабильный release запрещён до TESTING.md/ROADMAP игровых критериев.

Ключи пока не зашифрованы на диске: доступ к Windows-профилю и backup private keys означает владение этой личностью. Marker блокирует пропажу key, если marker сохранился; удаление всего identity каталога локально не обнаруживается. Новый key не получает старую company по имени. Подробности [PR #8](docs/STAGE1_PERSISTENT_OWNERSHIP.md).

## Риски, обнаруженные первым совместным испытанием

[PLAY-01–08](docs/COOP_PLAYTEST_2026_10_15.md): большой save может загружаться дольше прежнего deadline; заполнение bulk queue мешало control traffic. Исправленный резерв конечен, FIFO backlog и upload скорость нужно измерить. Прежний отказ около 50% не привязан к одному найденному дефекту без свежих logs.

Один saved subsidy claim предотвращает повторный вызов handler в component tests, но не исправляет premature native apply и не откатывает частичные rewards при engine exception. Simultaneous accept/restart/delivery — game gate. Новые road/station/orders/privacy адаптеры и UI реализованы в PR #11; [точные ограничения](docs/BUSINESS_MECHANICS.md).

- Engine-public road с logical owner — вынужденный адаптер единственного PLAYER_OWNED. Все штатные builders должны пройти проверку: если native pre-build param не меняет реально применяемый proposal, запрет не обеспечен. Не заявлять защиту от злонамеренного клиента/полный ACL по UI fixtures.
- Geometry matching с допуском 0.5 м требует проверки curved/split/overlap/bridge/tunnel/edit/delete. Штатное содержание engine-public дороги может отличаться от MAINTENANCE_COST; ручную компенсацию сверять с journal, не выдавать её за измеренную эквивалентность solo.
- Проводки debit/credit не атомарны. Callback error/потерянное подтверждение/save между ними блокирует leg. Успешная leg не повторяется; submitted после reload — uncertain, нужна сверка. Очередь bounded; частичная оплата не считается завершённой.
- Delivery token опирается на local sim entity/pickup/line/stop/roundtrip; accepted cargo event должен соответствовать реальной доставке. Разные формы payload, reused entity id, transshipment, ticket event timing и пассажиры — обязательные game tests. Client delivery operation не разрешена.
- GUI projection не является секретностью от клиента с полной копией мира. Unknown/global notices сохраняются, чтобы не скрыть нужные события. Direct GUI/native paths и звук должны быть проверены на точной версии.
- Money filter касается GUI positions, не accounts/journal. Если native destination world alias source, он ничего не очищает; видимость foreign popup при таком build остаётся game compatibility issue. Новых RVAs/подписей не вводили, known-build gate сохраняется.
- Launcher liveness доказывает только связь, не engine progress. Бюджет конечен (2 часа); зависший движок всё равно требует logs/профилирования. Root cause исходного 50% ещё не доказан.
- Каталог/road matching/vehicle scan не профилировались на Gigantomaniac. Saved limits и GUI cache уменьшают рост, но не доказывают приемлемый game step/RAM/IPC.
