# Первое игровое подключение: сессия не стартовала

14.10.2026. Испытание выполняли заказчик (хост) и его брат (клиент) на своих Windows-компьютерах. Я анализировала предоставленные журналы и изображения; локально игру не запускала. Результат: **FAIL — совместная игра не началась**. Успешные offline tests не закрывают этот отказ.

Исходники для сопоставления: main `f71d64d08b60f74368d7ba6af0e778e059a20387`, после PR #9. На снимке лаунчер имеет версию `0.3.6-experimental`, в logs создаются ownership profiles. Exact EXE/DLL hashes, полный game build и активные game mods на обоих ПК пока неизвестны; равенство версии в заголовке не доказывает равенство пакетов.

Обновление по дополнительным host session logs: native stamp `6ac50427` распознан кодом как 40420, native hooks и UI interception установлены. `auto.log` показывает чтение test save и вызов загрузки с MPFever, добавленным к mods. В предоставленном наборе нет bridge `mod.log`/`out.log`, а launcher не получил hello. Найден и исправлен конкретный дефект game resource catalog, описанный ниже. Положительный игровой повтор после исправления пока **NOT RUN**.

## Предоставленные доказательства

- Три launcher logs хоста: `launcher-20261014-214751.log`, `launcher-20261014-215357.log`, `launcher-20261014-215814.log`.
- Launcher log клиента: `launcher-20261014-144724.log`. Часовые пояса ПК неизвестны, их локальные часы нельзя напрямую сравнивать.
- Снимок ошибки клиента: не удалось прочитать данные транспортного соединения при подключении к публичному IPv4 хоста, TCP 28090.
- Снимок лаунчера хоста: сервер слушает, ноль готовых игр, «Ни одна игра не готова», в доступных адресах видны Radmin IPv4 и LAN IPv4.

Полные logs и изображения не публикуются: они содержат адреса, локальные paths, Steam profile identifiers и owner fingerprints. Имена игроков и конкретные адреса в этом документе заменены ролями/placeholder. Private identity keys запрашивать или публиковать нельзя.

## Что установлено

1. Лаунчер хоста находит игру и копирует Lua-мод в Steam userdata mods. После выбора сохранения запускается TCP listener на 28090; listening не означает, что загруженная карта готова.
2. В трёх журналах нет принятого входящего TCP peer, cryptographic join или сообщения `hello` игрового bridge хоста. Нет подтверждённого успешного подключения клиента.
3. Separate и shared mode показали одинаковое отсутствие готовой игры хоста. По этим данным нельзя объяснить отказ именно системой отдельных компаний: до её проверки сессия не дошла.
4. Клиент принял Steam invitation с публичным IPv4. Он не пытался подключиться к Radmin IPv4, показанному на снимке хоста.
5. Текст ошибки относится к чтению NetworkStream. Это не доказательство только connect-timeout/закрытого порта. TCP мог соединиться с другим endpoint/перехватчиком или не получить ответ application handshake; нужна проверка конкретного адреса и текущий client log.

## Подтверждённый дефект упаковки и исправление

Bridge загружает `mpfever_permissions.lua` через `ug_require`, сначала относительным именем, затем `mpfever_1::/mpfever_permissions.lua`. Этот модуль был добавлен при исправлении прав, но не включён в `_content.json`. В старой main ресурсный каталог перечисляет 14 файлов, тогда как source content и реальный ZIP содержат 15; единственное расхождение — permissions module. UI script этот модуль не требует, поэтому его работа совместима с отказом загрузки bridge до его startup log/hello.

Прежняя lockstep harness читала четыре зависимости непосредственно с диска и не учитывала каталог. Теперь `Game` предоставляет Lua только зарегистрированные в `_content.json` ресурсы. При прежнем каталоге настоящая bridge source останавливается на namespaced permissions require, не создавая startup mod.log/hello. Эта модель разрешения ресурсов и engine API fixtures не являются запуском TF3.

Production исправление ограничено добавлением `mpfever_permissions.lua` в `_content.json`; native addresses, TCP protocol 4, RSA profiles, Lua gameplay/finance/order logic не менялись. Все 15 content files должны быть зарегистрированы. Новые tests проверяют полный каталог, startup/hello настоящего Lua bridge с injected API и отказ при намеренном отсутствии permissions. Windows verifier проверяет **собранный ZIP**: отсутствие незарегистрированных файлов, существование всех зарегистрированных ресурсов, их SHA-256 против source и сохранность catalog/license/PE/exports.

Перед исправлением [run 38567631668](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38567631668), test head `3ac3f438`, провалил Linux/Windows Lua jobs (missing module и catalog mismatch) и full Windows package verification: `Packaged content is not registered in game catalog: mpfever_permissions.lua`. C# component/decoder jobs прошли. Это воспроизведённая ошибка пакета, не ослабленная schema и не malformed fixture.

После исправления [run 38568193133](https://github.com/WestDed/TransportFever3-Multiplayer/actions/runs/38568193133), code head `485974f58188e41b9f7e89c6834b07e3027b4ad1`, все 7 jobs успешны: 82 Python/Lua tests на Linux/Windows (локально также 82 PASS), все 17 прежних lockstep scenarios сохранены; 323 C# checks на net8 Linux/Windows и actual net48 Windows; 50 decoder checks; real TCP benchmark; полный Windows EXE/DLL/package и все 15 registered content resources/hashes. Финальный head с документацией и его отдельный CI run фиксируются в PR #10.

Подтверждены дефект упаковки и его устранение в новом архиве. Соответствие реальным симптомам сильное, но окончательная причинная проверка — повтор игры или game stdout с исходным require error. Первоначальный игровой результат остаётся FAIL; CI не переводит его в PASS.

## Что остаётся проверить

- Неверный маршрут через Steam public invitation при запланированной игре через Radmin; firewall, NAT/forwarding или другой listening endpoint. В коде Steam invitation использует `PublicAddress()` (ipify), Radmin адрес автоматически не выбирается.
- После полного обновления пакета на обоих ПК подтвердить, что game-script bridge действительно загружается, пишет mod.log и hello, а host ready count становится минимум 1.
- Если hello всё ещё отсутствует, исследовать remaining game-script activation/API error по штатному game stdout. `mpfever_auto.lua` пишет в `auto.log`, bridge — в `mod.log`; ошибок до успешного require может не быть в mod.log.
- В данном host session unsupported native build не подтверждается: stamp соответствует известному 40420, hooks установлены. Это ещё не acceptance правильности каждого hook и игровых действий. Client native build/log пока не представлен.

Оставшиеся причины не объявляются установленными. Новый пакет исправляет подтверждённый resource catalog defect. Адреса hooks и game API по неподтверждённой гипотезе не менялись.

## Проверка по шагам

1. Закрыть игру и MPFever на обоих ПК, распаковать **один и тот же исправленный полный пакет** из PR #10 и запускать MPFever.exe именно из новой папки. Сохранить имеющиеся saves/ownership profiles. Installer обновляет мод; удаление identity/private keys не требуется. Выбрать тестовое сохранение и режим компаний. В launcher log должно появиться сообщение «игра готова» для самого хоста; при этом ready count станет минимум 1. Повторное нажатие Start при count=0 не устраняет отсутствие bridge.
2. Убедиться, что оба ПК подключены к одной сети Radmin. Пока использовать прямое подключение из MPFever к `<HOST_RADMIN_IP>:28090`; invitation с public IPv4 — другой маршрут и сам по себе не создаёт туннель через Radmin.
3. Пока host listener работает, на ПК клиента проверить TCP endpoint:

```powershell
Test-NetConnection <HOST_RADMIN_IP> -Port 28090
```

`TcpTestSucceeded=True` подтверждает TCP до endpoint, но не RSA/protocol join, загрузку сохранения или игровой lockstep. При False проверять Radmin связь/адрес/listener/firewall. Ping отдельно не доказывает доступность TCP 28090.

4. После прямой попытки собрать новый client launcher log и host session folder из `%TEMP%\mpfever\<host-session>`. Нужны `auto.log`, `native.log`, `mod.log`, `menu.log`, `menu_state.txt`, `menu_loaded.txt`, `identity.txt`, `in.log`, `out.log` — если созданы. Отсутствие файла тоже диагностический результат. Эта временная папка не является `%LOCALAPPDATA%\MPFever\identity` и не содержит ownership private profiles.
5. По auto/menu/native/mod событиям определить первый отказ, добавить воспроизводимую регрессию к конкретному компоненту, затем делать code fix отдельным PR. Повторить полный **игровой** запуск с тем же save/build и обоими ПК; совпадение hash и offline PASS не заменяет ready/gameplay acceptance.

## Ограничения текущей диагностики кода

`MainForm.OnMenuRequest` помещает join exception в menu_state UI, но не записывает его в launcher log. Поэтому предоставленный client log заканчивается ownership profile без текста ошибки, видимого на снимке. `GameLink` читает out.log, а auto/native/mod startup logs остаются раздельными. Это пробел наблюдаемости; он не доказывает конкретную engine или network причину.

`mpfever_auto.loadSave/loadPending` имеет fallback загрузки без подтверждённых details/mod list при недоступности metadata. Для сохранения без MPFever это кандидат на отсутствие bridge; фактический выбор fallback в данном запуске должен быть подтверждён auto.log и состоянием карты.

Данные auto/native/UI папки получены и запись обновлена. После игрового повтора добавить результаты hello/ready/connection/snapshot/company по обоим ПК. До успешного запуска не отмечать engine host/client, save load, owner recovery, компании или native hooks как PASS.
