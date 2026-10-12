# Тестирование и границы доказательства

12.10.2026. TF3 не установлена, Steam/game API и настоящих сохранений нет. Локальная среда — Linux, Python 3.12, lupa 2.6 / Lua 5.2. C#/Windows/native проверки выполняются GitHub Actions; Windows/игра локально не запускались.

## Фактические автоматические проверки

На [run 38213663673](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38213663673), head b3f7dbc2: все шесть jobs успешны. 67 Python/Lua tests без exemptions, 210 C# component checks на Ubuntu/Windows, 50 LuaLit checks, полный net48 EXE и x64 DLL/14 exports/ordinals/package/license. Новые последующие изменения проверяются по финальному head в PR #5; нельзя переносить результат старого SHA на изменённый код.

| Проверка | Реальный код/среда | Не доказывает |
|---|---|---|
| Lockstep | Все 17 прежних assertions в исправленной harness, Lua bridge/ref/co | Native hooks, реальный темп/геометрия/callbacks |
| Decoder | Real Lua/C# grammar, malformed/expressions/limits/control/byte escapes | Полную schema и цену engine commands |
| Companies/permissions/Repay | Real modules/coLoanEvent с injected engine dependencies | PLAYER/ACCOUNT, формулу loan_util, progression/pathfinding |
| TCP/session | Real Relay/Client, handshake/capacity/credential/sender/negative controls | WAN/NAT/TLS/Steam account verification |
| Snapshots | Real source/queue/receiver/file, SHA/atomic/failure, 32 MiB x 1/4/8 | Игровой `.sav`, app.save/load и Gigantomaniac |
| Journal/recovery | Real disk/concurrent authors/hash/torn tail/replay/frontier/quorum | Игровое применение и atomic effects/frontier; scheduler не подключён |
| Windows build | Full existing VS build, AMD64 PE/exports/ordinals/ZIP/license | Исполнение DLL или правильность addresses/ABI в TF3 |
| Translations | 227 используемых keys/11 локалей и placeholders | Визуальный UI и все новые диагностические сообщения |

## Воспроизведение без игры

```sh
python -m pip install -r tests/requirements-offline.txt
python -m unittest discover -s tests -p 'test_*.py' -v
python tests/test_lockstep.py
python tests/test_lang.py
dotnet run --project tests/offline/Offline.csproj --configuration Release
dotnet run --project tests/offline/Offline.csproj --configuration Release -- --bench
```

Windows: `./tests/test_lualit.ps1`; `cmd /c build.bat`; `./tests/verify_windows_build.ps1`. .NET 8 SDK — для console suite; VS C++/MSBuild/.NET 4.8 pack — production build. Нет continue-on-error, expectedFailure или разрешённых translation gaps. Любая новая неожиданная ошибка проваливает job; исправление тестовой среды должно быть отдельно объяснено, не замаскировано exemption.

Workflow запускается на PR, main, workflow_dispatch; obsolete run отменяется. Artifacts: translation reports, benchmark JSON, experimental ZIP, build-manifest (gameTested=false), native exports. Хранение исходников/документов — Git branches. Benchmark sampling/method/raw results — [STAGE1_OFFLINE_RECOVERY](docs/STAGE1_OFFLINE_RECOVERY.md).

## После приобретения игры

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
