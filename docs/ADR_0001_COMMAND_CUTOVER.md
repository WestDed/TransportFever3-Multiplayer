# ADR 0001 — центральный порядок, граница снимка и переход движка

Дата: 12.10.2026. Статус: **offline foundation реализована; игровое включение отложено до engine gate**. MPFever сохраняется основой; переписывание сетевой архитектуры с нуля не выполняется.

## Причина

Сейчас issuer ставит `at`, оригинальная Lua/native логика может применить его действие до решения сервера, а relay отправляет действие другим игрокам. Нет server order/epoch в действующем исполнении; повтор `uid` с другим `at` и действие, пересекающее save/load, не имеют общей транзакционной границы. Подменить сортировку одним C# sequence недостаточно: native tools должны удерживаться до разрешения, callbacks могут приходить позже, а Lua UI команды и native proposal идут разными путями.

## Решение, подготовленное без игры

`CommandJournal` принимает уже подтверждённого peer и company binding, проверяет схему, присваивает общий sequence под lock. Перед возвратом acceptance пишет запись и `Flush(true)`. Возвращённый acceptance — факт записи входа, **не** вердикт двигателя и не списание денег. Повтор peer+uid с тем же payload возвращает прежний sequence; иное содержимое под тем же uid отвергается. Старая epoch не принимается.

Формат `MPFJ0001 + epoch` имеет ограниченные length-prefixed binary записи с previous/current SHA-256. Не используется BinaryFormatter или исполнение Lua. Budget: 256 MiB журнала, 100000 команд, frame до 4 MiB + 512 bytes; replay window до 64 записей, выдача по одной. В памяти — индекс offsets/digests, без всех payload. Это бюджеты безопасности, не замер стоимости 100000 записей.

Crash recovery восстанавливает полный prefix. Только физически незавершённый последний frame удаляется с сообщением `RecoveredTailBytes`; полный испорченный frame, неправильный size/hash/sequence/epoch/duplicate — отказ. Hash chain обнаруживает случайное повреждение и нарушение истории, не удостоверяет автора и не защищает от владельца диска, который перепишет цепочку. Силовой сбой диска/ОС и гарантии конкретной файловой системы не испытаны.

`Freeze()` атомарно пишет отдельный 80-byte marker epoch/frontier/hash. После restart такой журнал остаётся frozen, новый accept запрещён. Duplicate receipt остаётся доступным. Новая epoch получает другой journal path; публичного Unfreeze старой эпохи нет.

`ReplayCursor` проверяет epoch, непрерывность sequence, повторы/конфликтующие recent sequence. Ошибка callback блокирует курсор: эффект мог уже произойти, поэтому повторять его автоматически нельзя. Cache — 256 digests. Frontier **только в памяти**; durable atomicity «игровой эффект + frontier» им не обеспечивается.

`RecoveryBarrier` требует точный набор peers (включая host), epoch, snapshot hash и frontier. Дубликат ack не заменяет другого игрока; поздний ack не снимает failure. Нет resume по timeout или просто потому, что клиент прислал hello. Класс пока не подключён к существующему reload workflow, где receipt/hello остаются предварительными проверками.

## Последовательность игрового включения

1. Подтвердить текущие hooks на конкретном game build; неизвестная сборка должна отказывать до воздействия на симуляцию. Верифицировать capture/hold/release/callback для Lua actions и native construction отдельно.
2. Introduce versioned request `{epoch,uid,fn,args}` без преждевременного локального apply. Host проверяет membership, peer-company binding, допустимые refs/owners, prerequisites и правила цены; issuer получает тот же ordered replay, что остальные. Не исправлять неверного плательщика последующим balance correction.
3. Разделить accepted/applied/rejected. Engine refusal не превращать в successful journal effect. Callback/result связывать с epoch+sequence; зависимая команда не применяется до завершения необходимого create.
4. При freeze запретить новые requests и инструменты, закрыть outstanding callbacks/native releases, сверить applied frontier всех peers. Только затем save. Снимок содержит epoch/frontier и стабильные company/world IDs; server secrets в него не попадают.
5. Сопоставить снимок и отдельный durable manifest, в котором hash/frontier/epoch. После transfer/hash/atomic install выполнить load, восстановить local entity mappings и replay только после frontier. Engine-ready ack включает реальные проверки состояния, а не только receipt файла.
6. Сбой любой стадии сохраняет паузу и закрывает admission. Host restart начинает recovery; session credentials/claim migration должны быть подтверждены отдельно. Host migration не обещается.

## Игровые критерии приёмки

- Одновременные два автора, один порядок и одна запись эффекта/оплаты на каждом peer, включая автора; dependent create/update после callbacks.
- Разрыв до/после journal flush, delivery, callback и сохранения; 20 настоящих reconnect/resync циклов, без потери/двойного применения принятой команды.
- Crash host/client, old epoch/late callback, malformed command и insufficient funds. Неясный apply остаётся frozen до восстановления; новый snapshot не включает непринятые изменения.
- Upstream #15–22 и строительство на паузе/x1/x4 пройдены. Совпавший hash неправильного мира не считается корректностью.

Пока эти условия не выполнены, `CommandJournal`/`ReplayCursor`/`RecoveryBarrier` остаются отдельными проверяемыми компонентами. Их наличие в собранном EXE не означает, что центральный порядок уже работает в игре.
