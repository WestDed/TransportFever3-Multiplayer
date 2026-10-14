# Тестирование и границы доказательства

15.10.2026. В нашей среде TF3/Steam/game API не установлены. Локально Linux/Python 3.12/lupa 2.6, C#/Windows/native проверяются GitHub Actions. Заказчик и брат после PR #10 сообщили об успешном совместном startup/join через Porthole; большие карты/механики выявили отказы. Это пользовательский игровой результат, не мой запуск. [Полная запись, regressions и критерии повтора](docs/COOP_PLAYTEST_2026_10_15.md).

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

PR #10 восстанавливает packaging/loader fidelity: permissions module отсутствовал в `_content.json`, но старая harness брала его прямо с диска. Новая harness предоставляет только registered Lua resources; три catalog/bootstrap/missing-module tests дополняют 17 прежних lockstep scenarios. Windows verification сверяет все actual ZIP resources с catalog/source hashes. Baseline run 38567631668 упал на missing module и unregistered packaged content. После исправления code head 485974f5 прошёл [run 38568193133](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38568193133): 82 Python/Lua (локально тоже 82), 323 C# в каждой из трёх сред, 50 decoder, все 7 jobs и 15 registered resource/hash checks. Итоговый документированный head/run — в PR #10. Bootstrap использует real Lua source с injected engine API; реальный положительный game startup после исправления не проверен.

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
