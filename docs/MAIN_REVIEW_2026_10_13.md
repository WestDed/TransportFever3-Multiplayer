# Проверка main после постоянных владельцев

13.10.2026. Проверена main `5f6cd5845adb2cc59e49acfc436e77d80668994f` после слияния PR #8. Исправления — в отдельном [PR #9](https://github.com/WestDed/TransportFever3-Multiplayer/pull/9), ветка `fix/ownership-review`, непосредственно от этой main и прямо в main. Автоматическое слияние не выполняется. Transport Fever 3, игровые saves и native hooks не запускались.

Исходная main прошла [CI 38346446265](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38346446265): все 7 jobs успешны. Локально воспроизведены 78 Python/Lua tests. Зелёный исходный CI не покрывал перечисленные ниже отрицательные сценарии.

## Найденные ошибки

| Проблема | Поведение исходной main | Исправление |
|---|---|---|
| Неоднозначный владелец | `CompanyBindings.Resolve` учитывал только ready rows. Две компании с одним owner, одна ещё без engine entity, могли дать право на ready company. Lua lookup уже отвергал такого владельца, поэтому правила двух компонентов расходились. | Уникальность owner проверяется по всему host roster, включая pending и company 1, до разрешения claims. Несколько старых компаний без owner допустимы до явной миграции. |
| Частичный или ошибочный отчёт | `Report` заменял roster/world, затем изменял claims/owners по очереди. Ошибка на позднем claim оставляла часть нового состояния. Ошибка разбора до замены могла оставить прежние права действующими. | Новые bindings и reservations сначала рассчитываются отдельно; commit выполняется после проверки всех rows/claims. Любой отказ отчёта блокирует Require/Join/ValidateAssignment до нового корректного полного отчёта. Предыдущие bindings не изменяются частично. |
| Изменившийся заявленный Steam ID | Повторный `Join` требовал прежний sid даже с тем же доказанным private key. Появление sid после первоначального nil, изменение или временная пропажа при reload вызывали отказ и закрытие подключения. | Sid остаётся информационным полем. Повторный Join с прежним proven owner может обновить sid; чужой owner, forged key/host role/start money по-прежнему отклоняются. |
| Отсутствующие host/world при миграции | `CO.assignOwner` сравнивал world/approver на равенство без проверки их формы. `nil == nil` или одинаковые invalid строки позволяли helper назначить owner. | Lua helper требует корректные world (32 lower hex), saved host owner и approver (64 lower hex), их совпадение и прежние company/uniqueness ограничения. Отказ не изменяет registry row. |

Последняя ошибка воспроизведена непосредственно на Lua-модуле main: legacy row получала owner при отсутствующих host owner и world. Это дефект проверки внутри helper. Production C#/GUI host-only gates уже ограничивали источник команды; удалённый обход этих gates этим тестом не доказан.

## Изменённые компоненты

`launcher/ProtocolSchema.cs`: полная проверка owner uniqueness; staged binding commit; блокировка прав после invalid report; обновление sid при том же proven owner. `mod/mpfever_1/content/mpfever_co.lua`: обязательная host/world authority для назначения, общий validator world и уточнение описания постоянной личности. TCP-протокол, key format и игровой scheduler сохраняют прежний формат.

`tests/offline/CompanyRosterTests.cs` использует настоящую CompanyBindings: ready/pending duplicate, host/client duplicate, invalid/malformed report, запрет stale actions/new joins/migration, восстановление после valid report, отсутствие частичного назначения и изменение/пропажу sid. Файл вызывается обязательной suite и на net8 Linux/Windows, и на actual Windows Framework 4.8.

`tests/test_persistent_ownership.py` добавляет один test с пятью subcases: нет host/world, нет world, нет host, invalid world, invalid host. Все пять subcases падали на старом Lua helper и проходят после исправления. Существующие restart/key/legacy/funding regressions сохранены.

## Результаты и ошибка тестовых данных

Первый [run 38347775048](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38347775048), head `e8795e45`, добавлял новые tests к старому production code. Python jobs показали пять отказов assertions новой migration регрессии. C# jobs остановились раньше на неверной тестовой фикстуре: shorthand array не входит в data-only грамматику LuaLit. Этот C# отказ **не является доказательством ошибки production roster**. Фикстура исправлена на явные `[index]=row`; ограничения decoder не ослаблены. Ошибки C# roster выявлены анализом исходников и покрыты исправленной regression suite.

На [run 38348129418](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38348129418), code head `c5ae9be764b715254c940e6189e0642b7f71604f`, все 7 jobs успешны:

- 79 Python/Lua tests на Linux/Windows, без исключений; локально те же 79 проходят.
- 323 C# checks на net8 Linux, net8 Windows и **actual .NET Framework 4.8 Windows runtime**.
- 50 standalone C# decoder checks.
- Real TCP generated 32 MiB snapshot benchmark для 1/4/8 peers.
- Полная Windows net48 EXE/x64 DLL/exports/package/license сборка.

Следующий коммит обновляет только документацию. Итоговый точный head и его отдельный CI run фиксируются в PR #9; результат одного SHA не переносится на изменённый код. Workflow не получил continue-on-error, expectedFailure или иных exemptions: новые tests исполняются в существующих обязательных jobs и непредвиденный отказ проваливает проверку.

Воспроизведение:

```sh
python -m pip install -r tests/requirements-offline.txt
python -m unittest discover -s tests -p 'test_*.py' -v
dotnet run --project tests/offline/Offline.csproj --configuration Release
```

Windows: `dotnet build tests/offline/Offline48.csproj --configuration Release`, затем `tests/offline/bin48/net48/Offline48.exe`. Остальные команды — в TESTING.md.

## Границы проверки

Новые roster tests получают host registry как fixture, а не от реальной игры. Это проверка production C# policy и Lua registry helper; она не подтверждает PLAYER entities, сериализацию engine save, native order или game accounting.

При invalid roster блокируются новые проверяемые сетевые действия компаний. Уже применённое native действие автора не откатывается этой проверкой; premature apply и save/frontier cutover остаются в RISKS/ADR 0001. Реестр обязан быть корректным при новом полном report для восстановления прав; автоматическое исправление повреждённого save не добавлено.

Не проверены реальные save/restart/reconnect, engine legacy migration, совместимость hooks с установленной версией игры, WAN и Gigantomaniac. TLS/initial host admission, полные engine/ref/price permissions, независимый progression, Obtain и station visit billing остаются задачами ROADMAP. Main не объявляется безошибочной или готовой к игровому релизу по результатам этого review.
