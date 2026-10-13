# Постоянный владелец компании

13.10.2026. [PR #8](https://github.com/WestDed/TransportFever3-Multiplayer/pull/8), `fix/persistent-company-ownership`, от main 47604fbb, направлен прямо в main. TF3/Steam API/настоящие saves не запускались. Это реализация восстановления в production исходниках с offline доказательствами; игровой критерий остаётся NOT RUN.

## Что было неправильно

Старые session token, peer ID и CompanyBindings жили в памяти. После перезапуска хоста их не было. Lua-реестр сохранял компанию, но искал игрока по имени/заявленному sid; имя или присланный клиентом Steam ID не подтверждают владение. В результате восстановление и запрет захвата чужой компании нельзя было считать надёжными.

## Новая привязка

Игрок хранит постоянный RSA-2048 ключ в своём профиле Windows. SHA-256 отпечаток публичного ключа — `owner`, независимый от имени, адреса хоста и номера соединения. Лаунчер хоста использует отдельный постоянный ключ для компании 1.

Protocol **4**: server `challenge` содержит session epoch, случайный 256-bit nonce и публичный host fingerprint; клиент отвечает public modulus/exponent и RSA/SHA-256/PKCS#1 подписью фиксированного domain-separated transcript. Сервер вычисляет owner из проверенного публичного ключа, не принимает его как утверждение клиента. Private key не передаётся. Чужой private key, старый nonce, старый epoch или другой host transcript не дают той же подписи. Старый session token без соответствующего ключа не заменяет игрока.

После restart epoch меняется; Client сбрасывает token прежнего epoch и доказывает тот же постоянный key. Session peer ID/token могут поменяться — компания восстанавливается по owner. Новый клиент с прежним именем и тем же заявленным sid не получает старую компанию.

В сохранённом Lua registry каждая компания имеет `owner`; root содержит `ownershipVersion=2` и `world` (постоянный ID сохранённой карты). Они входят в shared signature. CO.match/CO.join в production используют owner. Registry row, company id, name/color/entity, loans, shares и финансы остаются прежними; GUI pump видит существующую entity и не запускает coCreate/стартовый капитал повторно. Host roster и CompanyBindings подтверждают именно этот owner перед действиями игрока.

## Профили и резервное копирование

Файлы автоматически создаются в `%LOCALAPPDATA%\MPFever\identity\`:

- `client.private-owner.lua` — личность игрока; сохранять при переустановке Windows/смене компьютера.
- `host.private-owner.lua` — личность хоста; нужна для прежней компании 1.
- `.lock` файлы — временные блокировки доступа, не личности.
- `.owner` файлы — публичные отпечатки: обнаруживают пропажу/неожиданную замену private key. Резервировать весь каталог identity; private key без marker импортируется на чистом профиле.

Private parameters находятся в файле ключа, не в save, журнале, протоколе или ZIP. Файл пока не зашифрован на диске: защищать доступ к своему профилю Windows, резервные копии хранить приватно. Не отправлять эти файлы в issues/логи/публичный GitHub. В логах показывается только публичный fingerprint.

Первое создание — temporary file + Flush(true) + File.Move без overwrite. Повреждённый/oversized/несогласованный key не регенерируется: запуск отказывает и сохраняет исходные bytes. При пропаже private key и сохранённом marker новая личность не создаётся. Удаление **всего** каталога не обнаруживается локально; новый ключ всё равно не получает старую компанию. Конкурентный writer не заменяет профиль. Потеря client key не исправляется совпадением имени: восстановить его backup. Перенос файла ключа на чистый профиль даёт новому компьютеру ту же личность; при имеющемся профиле сначала сделать его backup и явно выбрать импортируемую личность. Одна личность допускает одно активное соединение; отдельные тестовые игроки на одном Windows-профиле требуют отдельных ключей.

## Старые сохранения: явное назначение хостом

У старой компании нет cryptographic owner, поэтому автоматически угадать прежнего игрока невозможно. Host сначала загружает старую карту: компания 1 получает его owner и карта получает world ID. В журнале появляются `Saved ownership world`, `Company ownership roster` (company id/key/UNASSIGNED) и при подключении игрока `Player ownership fingerprint`.

Хост сверяет, кому принадлежит старая компания, и создаёт **локальный** `%LOCALAPPDATA%\MPFever\identity\company-assignment.lua`:

```lua
{["world"]="<32 hex символа из Saved ownership world>",
 ["id"]=2,
 ["owner"]="<64 hex символа нужного игрока из Player ownership fingerprint>",
 ["request"]="<новый идентификатор запроса: 32 hex символа>"}
```

Публичные id здесь не являются ключами. Для request можно использовать PowerShell: `[guid]::NewGuid().ToString("N")`. `id` — номер компании в нашем roster, **не** engine entity ID. HostLoop валидирует bounded data-only файл, текущий world, существующую готовую компанию, уникальность owner и отсутствие другого подтверждённого owner. Через host-only GUI directive создаётся ordered native `mpfCoAssign`; SIM обновляет только owner той же записи. Время применения использует текущий upstream scheduler — без включения нового journal/cutover.

Обычный клиент не может прислать directive/command миграции. Assignment для другой карты, компании 1, чужого подтверждённого owner или owner уже другой компании отказывает. Повтор того же назначения не создаёт компанию и не переводит денег. Дождаться нового owner в roster, **сохранить игру**, удалить файл запроса, затем переподключить игрока. Если клиент был отказан за legacy claim, autoreconnect даёт время выполнить назначение. Окончательный результат этого пути требуется испытать в игре.

## Доказательства и обнаруженная ошибка

Python/Lua suite проверяет сериализацию registry, смену имени, совпадение имени/sid с чужим ключом, отсутствие key fallback, explicit legacy assignment/world scope, duplicate owner, host role, signature и actual pumpCompanies без coCreate. Состояние balance/vehicles/loans в fixture — подставленные данные, не engine accounting.

C# suite использует actual RSA/profile/Relay/Client/CompanyBindings: реальные файлы и TCP, уничтожение старых endpoints, повторное чтение профилей, новый host epoch, renamed client, украденный token с чужим ключом, signature replay и migration restrictions. Дополнительно те же компоненты **исполняются на Windows .NET Framework 4.8**, не только собираются; net8 Windows/Linux проверки сохранены.

Первый run 38339664174: net8 suite прошла 302 checks, Python 77 tests, full Windows build; net48 execution выявило фактический 1024-bit modulus после KeySize setter у default CSP. Ограничение не ослаблено: используется явный RSA_AES provider (type 24), размер задаётся в constructor и проверяется при export. Intermediate net48 paths перенесены в Directory.Build.props. Итоговый CI head/run и counts смотреть в PR #8.

## Что ещё не доказано

Это possession proof собственного ключа, не Steam ticket verification и не initial host invitation. Транспорт остаётся plaintext: proof не обеспечивает confidentiality, server authentication или защиту последующих сообщений от активного MITM. TLS остаётся следующей задачей; публичный интернет-релиз не готов. Локальный host/key/save владелец имеет контроль над файлами.

Реальная сериализация owner/world движком, PLAYER/JOURNAL entities, GUI/native save-load/reconnect timing, остановка/запуск хоста и отсутствие повторного списания должны пройти игровой стенд. Полный engine cutover и атомарность save/frontier остаются отдельным ADR. Успешный mock/socket test не закрывает этот критерий.
