# Исправления, проверяемые без игры — 16.10.2026

Пакет **0.3.7-westded.1-experimental**, protocol **6**, FileVersion **0.3.7.1**. Это наша ветка, не авторская 0.3.7/0.3.11. Основа — открытый [PR #12](https://github.com/WestDed/TransportFever3-Multiplayer/pull/12), включающий #11. Изменения сохраняются отдельным PR, main напрямую не меняется, автоматического merge нет.

Заказчик разрешил выполнить пункты, которые можно содержательно проверить без игры; изменения, требующие игрового стенда для определения правильного поведения, оставить без изменений. Исправлены данные, очереди и локальный интерфейс. Native hooks, новые engine proposals и финансовые формулы не менялись. Компонентный PASS не доказывает работу hooks/GUI/pathfinding в TF3.

## Выполнено

| Пункт аудита | Результат и проверка |
|---|---|
| Числа/локаль (#30) | `%.0f` для целых и hash, canonical zero; fixed byte classes в encoder **и decoder**. Large integer tests моделируют Windows 32-bit `%d`; UTF-8 проверен при смоделированной locale classification. Прежние hostile-input/all-byte tests сохранены |
| Waypoints (#27) | Typed Waypoint/EdgePos/EdgeId, geometric track refs, optional разрешён только внутри настоящего line command; разворот param без изменения исходных args. Fixtures проверяют missing/reversed track и повтор rebuild |
| Line hash | Учитывает track geometry/param; raw ids исключены, противоположная ориентация нормализована. Разные tracks/param дают разные hash, перенумерация entities не влияет |
| Поиск refs (#25) | Один spatial index на translateIn и memo повторов, cache не живёт после команды, bindings приоритетны. На 1000 nodes/999 edges и 200 парах refs: не более 1 map scan и менее 6000 component reads; результаты сверены с linear search. Это счёт операций, не FPS |
| Тяжёлый hash (#32) | Группы меняются по номеру checkpoint; money/дешёвые части проверяются всегда. Host сохраняет difference до новой проверки этой части, считает её собственные observations, игнорирует старый ответ. Full determinism/autotest не урезается. Проверены rotation, error cleanup, late replies, reset и memory bounds |
| Покупки (#35) | Saved `st.buyAt` ограничивает покупки одним game step; vehicle-watch timeout тоже считает game steps. Проверены два update на одном step, следующий step, serialization/reload и idle calls |
| Покупка → линия на паузе (#36) | Зависимые actions ждут callback vehicle/line creation. При пропаже ответа recovery запрашивается один раз; очередь не продолжает зависимые действия. Есть async/sync/no-answer/stale callback regressions |
| Быстрые edits (#19) | Lua native queue ждёт существующие replayOut/quiet flags; mpfCoAssign path сохранён. Actual runNativeReplays проверен с callback fixtures. **Частично:** native hold/capture timeout и геометрия остаются игровыми gates |
| Host restart (#29) | Load identity привязан к transfer token, а не повторяющемуся counter; save request/ack тоже имеют token. Общий claim key bridge/UI, новый menu marker при id=1. Actual handlers/menu loader проверены на настоящих временных файлах; C# control payload проходит real decoder |
| Скорость (#24) | Каждому запросу возвращается callback, UI показывает session speed. Bridge читает real engine speed, включая 0, если API table общая. Actual install/gameSpeed tests проверяют callback/throttle, stale status и session 4 против engine 0 |
| Имя (#39) | Launcher name field и menu GameLink используют сохранённое имя с fallback. Read/review + Windows compile; настоящий Windows Form не запускался |
| Окно новостей (#37/#38) | Перехватывается close callback, вызывается перед уходом main page; manual close очищает запись. Actual recipe/timer tests: once-only, missing/throwing callback. Renderer не запускался |
| Уведомления (#42) | Локальные dismiss/enlist и log filter переживают resync/UI reload, применяются исходными **локальными** Notifications commands и не пересылаются peer. One-shot использует tolerance 120000 по timestamp, persistent — содержание. Проверены новые local ids, enlist, time window/filter, key types/order, циклические данные, module retry и failed write |
| Ограничения preferences | Не более 512 keys, 16 timestamps/key, 2048 bytes/key, 2 MiB/file, filter 64 KiB; новые выборы вытесняют старые. За poll — последние 4096 history entries. Corrupted/oversized data не скрывает сообщения |
| Совместимость | WestDed suffix, protocol 6, сверка launcher/package version в ZIP verifier. Real TCP negative case меняет только protocol у иначе корректно подписанного join |

Адаптированы выбранные исправления/идеи [dkdknight/MPFever `171d35c`](https://github.com/dkdknight/MPFever/tree/171d35c19574f1b8fdf1bc8f174755f30ebb54bc), включая PR Steelworm #38–42, MIT. Авторство и LICENSE сохранены. Не заменяли common/native целиком: наши data-only parser, bounded IPC/TCP, RSA ownership/roster/schema, snapshot SHA/atomic commit/receipt/liveness, saved Repay terms, business и destination-only money filter сохранены.

## Оставлено без изменений по ограничению заказчика

| Пункт | Почему требуется двигатель |
|---|---|
| Снос станции (#15), tunnel/bridge/signal/node config (#16/#26/#28) | Native proposal/removal shapes, реальная геометрия и engine assertions |
| Стройка во время reload (#17), held-build state/ring/release id (#18) | Save/load boundary, две native копии мира и удержание author build |
| Tool context/replacement maps/town buildings (#20, 0.3.9) | Engine определяет costs, удаление домов и entity numbering |
| Marshal userdata cache (#21), Send To Line callback shape (#22) | Настоящие game userdata и GUI casts отсутствуют |
| LineViewer native hook | Signature/RVA/ABI проверяются на точном game build |
| Terrain lead (#41), fast stop (#34/#40), open #43/#44 | Native pacing/capture handshake; быстрые переключатели не включены |
| Company actions, XP/rank, shared depots, Obtain terms, новые свободные переводы | Действительные modules/offers/formulas, ownership и journal reconciliation; Company/Companies guard не удалён |
| Авторский lobby, tutorial, ground paint | Новый load orchestration, mission scripts и native terrain APIs |
| Owner/accounts и road/station/order/privacy mechanics | Уже реализованный код оставлен; нужен прежний game acceptance, не новая перепись механик |
| 3+ игрока, WAN soak, Gigantomaniac | Реальные saves/traffic/RAM/FPS/game-step profiling |

Preferences находятся в `notif_preferences.lua` локального session directory игры, **не** в identity. Этот файл не передают другому игроку. Ошибка чтения оставляет сообщения видимыми. Сохранение косметических preferences не обещает атомарной записи при сбое питания и не относится к финансовому ledger.

## Проверка и продолжение

`python -m pip install -r tests/requirements-offline.txt`, затем `python -m unittest discover -s tests -p 'test_*.py' -v` и `python tests/test_lang_baseline.py --report`. Локально: **231 tests PASS**, **282/282 keys × 11 locales**, `git diff --check` PASS.

До fixes новые data tests имели 6 ошибок, reference — 4, scheduling — 6, resync — 3: это красные воспроизведения, не exemptions. Первая harness извлекала runPendingPaused вместо настоящей runPausedActions; исправлена до red baseline. Два native-queue tests также потребовали добавить отсутствующие fixture pumpTerrain/pumpCompanies; production guard не обходился.

Новые C# SnapshotControlTests/CheckpointTests/real TCP protocol case включены в net8 Linux/Windows и actual net48 Windows suite. Локального .NET SDK в среде нет; C# и Windows ZIP проверяет CI точного head. Итоговые counts/run/artifact — в PR после завершения проверки, до этого CI не объявляется успешным. Не переносить старые 363 checks/чужой green SHA на новый код. Workflow без continue-on-error/exemptions.

У обоих игроков должен быть **один полный** новый пакет и полный перезапуск игры. Protocol 6 отказывает прежнему 5: отличаются line hashes/waypoint refs, rotation и save-control ack. Не заменять отдельно EXE/Lua.

Далее на игровых ПК: owner/balances до/после полного host restart, повтор id=1, waypoint после edit/rejoin, покупки pause/x1/x4, быстрые edits, скорость от другого игрока, dismissed notices после resync. Большой мир: hash/capture/load latency. Ни один из этих игровых сценариев не объявлен PASS данным документом.
