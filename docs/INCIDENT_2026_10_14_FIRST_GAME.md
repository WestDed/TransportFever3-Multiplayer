# Первое игровое подключение: сессия не стартовала

14.10.2026. Испытание выполняли заказчик (хост) и его брат (клиент) на своих Windows-компьютерах. Я анализировала предоставленные журналы и изображения; локально игру не запускала. Результат: **FAIL — совместная игра не началась**. Успешные offline tests не закрывают этот отказ.

Исходники для сопоставления: main `f71d64d08b60f74368d7ba6af0e778e059a20387`, после PR #9. На снимке лаунчер имеет версию `0.3.6-experimental`, в logs создаются ownership profiles. Exact EXE/DLL hashes, полный game build и активные game mods на обоих ПК пока неизвестны; равенство версии в заголовке не доказывает равенство пакетов.

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

## Что остаётся гипотезой

- Неверный маршрут через Steam public invitation при запланированной игре через Radmin; firewall, NAT/forwarding или другой listening endpoint. В коде Steam invitation использует `PublicAddress()` (ipify), Radmin адрес автоматически не выбирается.
- Host save не загрузился автоматически либо загрузился без активного game-script MPFever.
- Application script/metadata load/GUI bridge получили Lua/API error. `mpfever_auto.lua` записывает такие ошибки в `auto.log`, bridge — в `mod.log`; ошибок до успешного require может не быть в mod.log.
- Game/native build или неполная установка. Native startup сообщает PE stamp/patches и `unknown game build: hooks off` в native.log; этих данных пока нет. Наличие main-menu MPFever UI само по себе не подтверждает работу game-script bridge после загрузки карты.

Это рабочие версии для проверки, а не подтверждённые причины. Адреса hooks и game API вслепую не менялись; новые бинарники для «исправления» неизвестной причины не выпущены.

## Проверка по шагам

1. Запустить хост, выбрать тестовое сохранение. Уточнить: появилась ли сама карта или игра осталась в главном меню. В launcher log должно появиться сообщение «игра готова» для самого хоста; при этом ready count станет минимум 1. Повторное нажатие Start при count=0 не устраняет отсутствие bridge.
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

После получения диагностической папки обновить этот документ и HANDOFF. До успешного запуска не отмечать engine host/client, save load, owner recovery, компании или native hooks как PASS.
