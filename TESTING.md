# Тестирование и границы доказательства

20.10, .7/protocol 12: [регрессии подключения и паузы](docs/CONNECT_TIME_AND_STUCK_PAUSE.md). TransferResilienceTests (сжатие, враждебный DEFLATE, приём >40 мин, сохранение частей при простое, TransferProgress, BulkWritten на TCP) и RecoveryGateTests; 730 C# checks net8 Linux PASS, 305 Python/Lua PASS, net48 build. Новые тесты падают на старом коде (мутационная проверка). Игра не запускалась.

19.10, .6/protocol 11: [новые регрессии и игровой повтор](docs/FASTER_CONNECTIONS.md). SnapshotResumeTests/LobbyPreparationTests исполняют настоящие компоненты и TCP: повтор по новому токену, missing-only, corruption, изменённый same-size мир, crash-complete SHA, отмена и два барьера запуска. WinForms проверяет фактический received-state путь без TF3. Exact CI/head/build фиксируются в PR; игра разработчиком не запускалась.

19.10: [матрица лобби/кеша/reconnect и baseline](docs/LOBBY_AND_SNAPSHOT_CACHE.md). 296 Python/Lua/C++ checks локально PASS; 299/299 ключей ×11 локалей. C# проверяет production sockets/файлы на net8 Windows/Linux и реальном Windows net48; точные результаты final head/Windows ZIP — в PR #18. Baseline 39139322765 подтверждает speed=3 на неизменённом main. Wiring checks не являются GUI или TF3 tests. В Windows build дополнительно исполняется настоящее окно WinForms с отключённым Shown→Init: состояния кнопок, actual cancel, повторное присоединение и смена en/ru; реальные queued callbacks отменённой сессии/потерянного соединения/другой комнаты и явный retry при невозможном пути EXE. Это проверка лаунчера, не движка; игровая/DPI приёмка остаётся обязательной.

18.10: [новые outbox regressions](docs/INCIDENT_2026_10_18_SNAPSHOT_QUEUE.md). Production C# writer тестируется при заблокированной записи и непрерывном потоке статусов: собственные clock keys, bytes/frames/in-flight limits, close, отказ replacement, порядок при session transitions и каждом ordered frame. Базовый runtime main c06a075 должен падать на flood case; итоговый head/runs/counts — в PR #17. Реальный 32 MiB TCP bench × 1/4/8 теперь включает concurrent periodic state traffic; это loopback, не игра/Porthole. Python suite остаётся 289.

18.10.2026: [регрессии вылета карты](docs/INCIDENT_2026_10_18_MAP_LOAD.md). Восемь новых tests запрещают использование callback handle после возврата и проверяют текущий ECS registry при другом мире/неполной загрузке, native update/price/metadata/readers и bootstrap настоящего wrapper. Весь suite — 289 Python/Lua/C++ tests; результат exact-head CI/Windows package фиксируется в PR. Это модель ограничения, не исполнение native activeCount assertion. Пользовательский crash .2 — подтверждён, игровой повтор .3 ещё NOT RETESTED.

16.10.2026, PR #15: [матрица личного развития](docs/COMPANY_PROGRESSION.md#приёмка-в-игре). Локально 281 Python/Lua/C++ tests PASS, 288/288 keys × 11 локалей, git diff --check. Новые проверки: миграция, личные XP/уровни, настоящий Lua scheduler, цена по владельцу линии, bounded dedup, native hash normalization, permits census failure, GUI/view/schema. C# и Windows exact-head CI фиксируются в PR #15 после завершения. **Game NOT TESTED для новых изменений.** Сведения ниже о PR #14 и более ранних heads — исторические.

16.10.2026: [новые offline regressions и границы](docs/OFFLINE_UPSTREAM_FIXES.md). Локально 231 tests PASS, 282/282 переводов × 11 локалей. Production functions проверяются на constructor/clock/async/React fixtures и настоящих temporary files; game engine не запускался. Новые C# cases включены во все три runtime/OS jobs, Windows ZIP имеет WestDed номер/protocol 6. Exact итоговые head/run/counts — в PR после CI, до этого проверка не объявляется успешной.

15.10.2026. В нашей среде TF3/Steam/game API не установлены. Локально Linux/Python 3.12/lupa 2.6, C#/Windows/native проверяются GitHub Actions. Заказчик и брат после PR #10 сообщили об успешном совместном startup/join через Porthole; большие карты/механики выявили отказы. Это пользовательский игровой результат, не мой запуск. [Полная запись, regressions и критерии повтора](docs/COOP_PLAYTEST_2026_10_15.md).

## Ручной тариф в окне остановки

Ветка `feat/custom-station-fee-2026-10-16` продолжает ещё не объединённый PR #11. `test_station_fee_ui.py` исполняет **настоящие** helper/recipe/sendShare из `mpfever_ui.script.lua` и `shareFee` из `mpfever_common.lua`, подставляя React и engine dependencies. 13 новых сценариев: кнопка/Enter, typing без отправки, произвольный тариф, UI-ноль → wire `-1` (бесплатно) против private preset `0`, граница 1e9, неверные числа/выражения/длинный ввод, исправление ошибки, неизменённая сумма, другая остановка, read-only чужая остановка, default 100, external updates/draft, pending ack/timeout и reopening nonpreset fee. Fixtures поддерживают сброс state в nil, чтобы проверить очистку draft/pending, а не скрыть её ошибку.

Локально: **175 tests PASS**, включая прежние 162; переводы **282/282** для каждой из 11 локалей; `git diff --check` PASS. Первоначально отсутствовала зависимость lupa в локальной Python-среде; установка точного `tests/requirements-offline.txt` восстановила harness без изменения требований или исключений. CI и полный Windows ZIP проверяются на точном опубликованном head; ссылки и результаты — в новом PR.

В игре ещё **NOT RETESTED**. На двух ПК проверить: свою сумму `1234` кнопкой и Enter; одинаковый тариф в обоих окнах и платёж ровно 1234 при визите чужого транспорта; свои визиты бесплатны; `0` бесплатно, пункт «Не общая» запрещает доступ; неверный ввод не меняет тариф и не разрывает связь. Повторить после закрытия окна, смены остановки, save/host restart и reconnect. Проверить ширину/фокус/Enter/цвет ошибки в настоящем GUI; офлайн recipe не проверяет rendering и native widget callbacks. Использовать один полный новый пакет на обоих ПК.

## Фактические автоматические проверки

На [run 38318567426](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38318567426), head 683328da (PR #6): все шесть jobs успешны. 67 Python/Lua tests без exemptions, 276 C# component checks на Ubuntu/Windows, 50 LuaLit checks, полный net48 EXE и x64 DLL/14 exports/ordinals/package/license. Интеграция в [PR #7](https://github.com/WestDed/TransportFever3-Multiplayer/pull/7) сохраняет исходники/тесты/workflow этой версии; CI итогового integration head фиксируется в PR #7. Нельзя переносить результат старого SHA на изменённый код.

| Проверка | Реальный код/среда | Не доказывает |
|---|---|---|
| Lockstep | Все 17 прежних assertions в исправленной harness, Lua bridge/ref/co | Native hooks, реальный темп/геометрия/callbacks |
| Decoder | Real Lua/C# grammar, malformed/expressions/limits/control/byte escapes | Полную schema и цену engine commands |
| Companies/permissions/Repay | Real modules/coLoanEvent с injected engine dependencies | PLAYER/ACCOUNT, формулу loan_util, progression/pathfinding |
| TCP/session | Real Relay/Client, handshake ingress/pending isolation/first-frame ordering/pipelining/capacity/credential/sender/negative controls | WAN/NAT/TLS/Steam account verification |
| Snapshots | Real source/queue/receiver/file, SHA/atomic/failure, 32 MiB x 1/4/8 | Игровой `.sav`, app.save/load и Gigantomaniac |
| Journal/recovery | Real disk/concurrent authors/hash/torn tail/replay/frontier/quorum | Игровое применение и atomic effects/frontier; scheduler не подключён |
| Windows build | Full existing VS build, AMD64 PE/exports/ordinals/ZIP/license | Исполнение DLL или правильность addresses/ABI в TF3 |
| Translations | 227 используемых keys/11 локалей и placeholders | Визуальный UI и все новые диагностические сообщения |

Изоляция и пределы рукопожатия: [STAGE1_HANDSHAKE_ISOLATION](docs/STAGE1_HANDSHAKE_ISOLATION.md); результаты нового C# кода по точному head/run в PR #6.

PR #8 расширяет suite постоянными key/owner/restart/migration сценариями и actual Windows .NET Framework 4.8 execution. Net8 проходит на Linux/Windows; net48 suite исполняет production RSA/TCP/file классы. Точные итоговые counts/head/run в PR #8; game NOT RUN. [Описание и backup](docs/STAGE1_PERSISTENT_OWNERSHIP.md).

PR #8 уже слит: main 5f6cd58 прошла run 38346446265 (7 jobs, 78 Python/Lua, 306 C# checks). [PR #9](https://github.com/WestDed/TransportFever3-Multiplayer/pull/9) добавляет owner/roster/migration регрессии. Code head c5ae9be7 прошёл [run 38348129418](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38348129418): 79 Python/Lua, 323 C# checks на всех трёх runtime/OS вариантах, 50 decoder checks и все 7 jobs green. Финальный head с документацией проверяется отдельно; точный run в PR #9. Первый C# прогон новых tests имел ошибку формата фикстуры и не считается доказательством failure production кода. [Полный разбор](docs/MAIN_REVIEW_2026_10_13.md).

PR #10 восстанавливает packaging/loader fidelity: permissions module отсутствовал в `_content.json`, но старая harness брала его прямо с диска. Новая harness предоставляет только registered Lua resources; три catalog/bootstrap/missing-module tests дополняют 17 прежних lockstep scenarios. Windows verification сверяет все actual ZIP resources с catalog/source hashes. Baseline run 38567631668 упал на missing module и unregistered packaged content. После исправления code head 485974f5 прошёл [run 38568193133](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38568193133): 82 Python/Lua (локально тоже 82), 323 C# в каждой из трёх сред, 50 decoder, все 7 jobs и 15 registered resource/hash checks. Итоговый документированный head/run — в PR #10. Bootstrap использует real Lua source с injected engine API; впоследствии пользователи сообщили об успешном startup/join после исправления; новые механики ещё NOT RETESTED.

## Воспроизведение без игры

```sh
python -m pip install -r tests/requirements-offline.txt
python -m unittest discover -s tests -p 'test_*.py' -v
python tests/test_lockstep.py
python tests/test_lang.py
dotnet run --project tests/offline/Offline.csproj --configuration Release
dotnet run --project tests/offline/Offline.csproj --configuration Release -- --bench
```

Windows: `dotnet build tests/offline/Offline48.csproj --configuration Release`, затем `tests/offline/bin48/net48/Offline48.exe`; `./tests/test_lualit.ps1`; `cmd /c build.bat`; `./tests/verify_windows_build.ps1`. .NET 8 SDK — для console suite; VS C++/MSBuild/.NET 4.8 pack — production build. Нет continue-on-error, expectedFailure или разрешённых translation gaps. Любая новая неожиданная ошибка проваливает job; исправление тестовой среды должно быть отдельно объяснено, не замаскировано exemption.

Новая обязательная framework-components job исполняет те же assertions на целевом CLR. Первый прогон выявил 1024-bit CSP key несмотря на KeySize setter; исправлен provider/constructor, ограничения не ослаблялись. Нет игровых assertions о штатных PLAYER/JOURNAL: injected balance/loan/vehicle facts отмечены как fixture.

Workflow запускается на PR, main, workflow_dispatch; obsolete run отменяется. Artifacts: translation reports, benchmark JSON, experimental ZIP, build-manifest (gameTested=false), native exports. Хранение исходников/документов — Git branches. Benchmark sampling/method/raw results — [STAGE1_OFFLINE_RECOVERY](docs/STAGE1_OFFLINE_RECOVERY.md).

## Игровые испытания на компьютерах заказчика

1. Записать game build/hash, DLL/EXE/mod/DLC hashes, save/seed, hardware, roles, network и дату. Исходный save сохранить отдельно. [Шаблон протокола](docs/GAME_TEST_RECORD.md).
2. Два процесса одного ПК, затем два ПК: inactive/active launch, known/unknown build, host/client, shared/separate. Unknown hooks отказывают, не дают частичную синхронизацию.
3. Upstream #15–22, по десять повторений: station deletion, tunnel/bridge/signals, modular edits, copied vehicle, line callbacks; pause/x1/x4, simultaneous/dependent builds. Census геометрии/owners/balances и callbacks.
4. Company matrix own/foreign build/edit/delete/fleet/routes/loans/contracts/progression/bankruptcy. Проверить месячную историю, источник дохода и плательщика без скрытого corrective money.
5. Две компании у одного завода/города: цены, интервалы, пересадки, stocks/onboard/deliveries/revenues; сопоставить штатному pathfinding того же мира.
6. Плата за инфраструктуру: own/foreign vehicle, zero/100/custom fee, полный круг, reorder/reroute/line switch/owner change/reload. Один visit/payment, сумма transfers ноль.
7. WAN latency 50/150/300 мс, jitter, bandwidth/loss через сетевой стенд; disconnect 5/30/300 с, full server, slow client. TCP loss даёт задержку/разрыв, не произвольную перестановку доставленных байтов.
8. Разрыв/crash на request/flush/replay/callback/save/chunk/load; 20 настоящих reconnect/resync. Old epoch/late ack, потерянный accepted command или double payment — fail. Failed peer не resumes.
9. Два игрока/2 часа, четыре/4 часа, затем 8-часовой soak. Ноль crash/unexplained desync; доказанная семантика, не только checksum.
10. Карты до Gigantomaniac: game steps/s vs solo, checksum p50/p95, RSS/peak, save/transfer/load time и host upload. Caps/пороговые цели пересмотреть по замерам.

Минимальная запись неуспеха: scenario, epoch/sequence/step/company, expected/actual geometry/owner/journal/payment, logs, число resync и длительность. PASS мока и compiled DLL не заменяют PASS игрового сценария.

## Первые gameplay regressions — PR #11

Code head `ebd3395f5e14b41d37e809a65aa0c6a4d4c36b84`, [run 38591306735](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38591306735): все 7 jobs success, 105 Python/Lua на Linux/Windows, 336 C# checks на net8 Linux/Windows и actual net48 Windows, 50 decoder checks, реальный TCP bench и полный Windows package verification. Локально 105 PASS. [Baseline red и подробные границы](docs/COOP_PLAYTEST_2026_10_15.md). Финальный documentation head/CI смотреть в PR #11.

Новые tests исполняют настоящие CO/UI Lua functions с injected module/owner/state, включая one-off subsidy, private cards с отсутствующими списками, позднюю station hook установку, cache refresh и UI reload. Queue тест насыщает bytes с blocked writer, deadline tests проверяют defaults/overrides/invalid input. MainForm receipt/load orchestration компилируется Windows job, но console suite её не исполняет. Game повтор на большом save и реальные grants/fees/notifications ещё NOT RETESTED.

## Полный пакет механик 0.3.7-experimental

[BUSINESS_MECHANICS](docs/BUSINESS_MECHANICS.md) содержит правила и восьмипунктовую игровую матрицу. Новые `test_business.py` и `test_business_adapters.py` исполняют production core, engine adapter и React window: права владельца, full-curve split против удаления середины, одно списание/повторный въезд, reserve funds, callback retry, uncertain reload, escrow/one carrier/cargo/passenger delivery/cancel/expiry, form controls и GUI notification projection. Существующий station selection suite дополнен free access.

`test_native_privacy.py` + `native_privacy_tests.cpp` исполняют настоящий C++ helper для own/foreign/duplicate/canonical/empty/invalid/alias cases, проверяют сохранность simulation map. Linux — g++; Windows full-build job — MSVC через `test_native_privacy.ps1`. Это isolated UI helper, не вызов game hooks. C# SchemaTests проверяют business whitelist, границы, spoofed actor/request/delivery и snapshot-specific load phases; SnapshotTests — soft/liveness/hard deadlines.

Первый полный run [38683104340](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38683104340) на head `3aa2b69df7f7b8032fcae36122d8608c5b11cc68`: Lua на обеих ОС, decoder и full Windows build/native filter PASS; три C# jobs законно FAILED на CS0128 — новое имя `now` пересекалось со старым fixture. Переменная нового теста переименована в loadStart; exemptions не вводились. После этого production C# components отдельно скомпилированы Roslyn 8.0 и исполнены на .NET 8 Linux: **363 checks PASS**. SDK CLI в этой sandbox не читает Process.StartTime; compiler/assembly запущены напрямую, это особенность локальной среды, не модификация production/tests. Обычный dotnet run и actual net48 обязаны пройти GitHub Actions.

Итоговый head/run и artifact фиксируются в PR #11 после полной проверки. Не использовать artifact раннего failed run как готовый результат. После последней правки локально требуется весь Python discovery; Windows/net8/net48/package/decoder результаты — на том же точном итоговом head. Игровой статус всех новых механик **NOT RETESTED**; пользовательский прошлый startup/join **USER-REPORTED PASS**, прежние отказы сохранены отдельно.

Дополнительное ревью: тариф после split сохраняет всю исходную геометрию; connect не допускает road template/lane/decorations upgrade; сравнение metadata не зависит от порядка table keys. При полной очереди уже доставленный заказ ожидает reward, а не возвращает деньги issuer по deadline; неоплаченная maintenance obligation не теряется. Для каждого случая добавлены регрессии. Локально **158 Python/Lua/C++ tests PASS**, C# **363 checks PASS**. Предыдущий полный head `001df1c042813a8400f2eb3ce6f37d73b8c5bf76` прошёл все семь jobs в [run 38683866783](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38683866783); последующие guard changes требуют собственного финального run, который фиксируется в PR.

Последняя проверка формы: пустой заказ/нечисловой тариф остаются локальной ошибкой UI, не отправляются как malformed action и не вызывают schema disconnect легитимного игрока. Исчезнувший target возвращает saved request_failed без escrow. Регрессии добавлены; локальный итог **161 tests PASS**. Эти UI changes также требуют финального CI на точном PR head.

CI report audit: `report_baseline` сохранял старое разрешение exit 1 от исходного translation checker, хотя список исключений давно пуст. Новый negative test воспроизвёл преобразование такого exit в success (`0 != 1`); исправлено на строгое `code != 0`, устаревший текст про три allowed keys удалён. Локально **162 tests PASS**; ни один nonzero checker result не игнорируется. Последний CI/head/package фиксируется в PR #11.
