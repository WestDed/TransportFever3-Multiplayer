# Этап 1: компании, переподключение и проверка команд

12.10.2026, [PR #4](https://github.com/WestDed/TransportFever3-Multiplayer/pull/4), зависимая ветка `fix/company-identity-schemas` над PR #3. Main не изменяется. Лицензия/авторство upstream сохранены; никакого запуска игры.

## Что изменено

- AUD-04 закрыт: `CO.signature` включает `sid`, стартовый капитал и число компаний. Поля имеют тип и длину: разделители в именах не создают одинаковую подпись разных реестров; nil отличается от строки `nil`. Локальные `ent`/`canon` исключены. Это контрольное представление данных, не криптографическая подпись личности.
- Expected failures полностью удалены; CI запрещает появление новых exemptions. Реальные Lua-модули и все 17 прежних lockstep assertions остаются обязательными.
- TCP-протокол **3**. Первичный join получает 32 случайных байта session credential; reconnect предъявляет его, сохраняет имя/identity и заменяет только свой старый сокет. Одинаковое имя без credential получает другую identity. Старое соединение при замене не создаёт ложный `peerleft`. Client сохраняет credential для автоматического reconnect в текущем запуске; ключ не записывается в чат/логи/снимок.
- Credential действителен только в памяти этой host session. После перезапуска хоста он не восстанавливается; миграция хоста не добавлена. TCP остаётся открытым: bearer token не защищён от наблюдателя сети. Это защита от подмены публичного имени, **не** Steam authentication и **не** безопасный доступ из недоверенной сети.
- `Relay` проверяет client kinds перед dispatch. Нельзя прислать `auth`, `session`, `resync_save/load/file`, `co_roster`, `welcome`, `save_done` и другие host controls. Native объявления требуют origin реального sender. Проверяются конечные точные integer, допустимые скорости, таблицы, лимиты строк и формы основных команд.
- Для действий обязательны `origin == sender`, `uid == sender:oseq`, положительный `oseq`, неотрицательный `at`, корректные `native/paused/co/args`. Клиентские команды ограничены флотом, линиями, стройкой, terrain/assets, company join и нужными scripting events. Journal/admin/world-time/town commands с клиента запрещены. События ограничены `Loan.Obtain/Repay`, `Subvention.onAccept/onDecline`, `mpfever.share`. Неизвестное действие требует явного добавления схемы и теста, не проходит произвольным factory lookup.
- Локальный **host** bridge сообщает `co_roster` со стабильными id/key/sid и readiness компании. Удалённый roster не допускается. `CompanyBindings` связывает session identity с записью хоста и сохраняет связь при reconnect. Клиент не может объявить себя host, увеличить стартовый капитал, указать другое имя, использовать co=1 или чужой подтверждённый id. Вложенные player refs сверяются с собственной компанией. В shared mode `mpfCoJoin` запрещён.
- Lua перед fleet/line apply проверяет владельца уже разрешённых локальных entities. Отказ при чужом или неизвестном владельце, чужой линии/депо, другом player при покупке/создании линии. Существующие правила платных станций остаются отдельно. Нет новых тарифов или изменения формулы цены.
- Repay использует сохранённые условия займа `lt.obtainedLoans[idx]`, а не присланную клиентом сумму/проценты. Повторный Repay после удаления записи не списывает деньги снова. Штатная `loan_util` formula вызывается как раньше. Получение займа (`Obtain`) пока требует анализа недоступного игрового loan_util/предложений; защита всех финансовых событий не объявляется законченной.

## Что проверяется

До этого PR было 44 Python/Lua теста с AUD-04 как expected failure. Сейчас локально проходят **60 тестов без exemptions**: подпись аккаунта/капитала/границ, прежний lockstep, pure ownership policy, Repay с подменёнными условиями/повтором/неизвестным займом. Loan test извлекает действующий `coLoanEvent` из bridge, заменяет только зависимости движка и formula входами/наблюдением. Он не проверяет настоящую формулу процентов.

C# suite компилирует производственные Core/NetworkIO/Snapshots/PeerIdentity/ProtocolSchema. Новые сценарии: реальные одинаковые имена, credential reconnect и forged token, host-only controls, malformed actions, origin/uid/co/player refs, незавершённая компания, сохранение binding при socket replacement. Production net48 EXE и native DLL по-прежнему собираются отдельно. Конкретный финальный Actions run и число checks фиксируются в PR после завершения CI.

Команды воспроизведения — TESTING.md и STAGE1_NETWORK_SNAPSHOTS.md. Парсерные и sockets PASS не являются игровым доказательством.

## Оставшиеся границы

1. `sid` всё ещё заявляется клиентом; родной Steam API этого клиента не является серверной проверкой билета. Первая привязка старой компании в новой сессии требует подтверждённой identity/host-approved migration. Реестр по имени без sid сохраняет исторический fallback и отдельно отмечен как небезопасный. `CompanyBindings` ограничивает действующих peers текущей сессии, не удостоверяет владельца сохранённой компании между сессиями.
2. Binding report/ready и permissions требуют настоящих GAME_SCRIPT/PLAYER_OWNED и callbacks. Возможна задержка готовности реестра: незавершённая привязка отказывает, не выдаёт права заранее. Игровые проверки должны измерить этот переход. Режим компаний остаётся experimental.
3. Shape validation не знает полной схемы engine Proposal/TransportVehicleConfig, reference поиска и стоимости. Lua ownership policy пока покрывает fleet/line; комплексный снос графа, terrain, native asset ownership, Public/unowned infrastructure и arbitrary mod scripting events требуют отдельной матрицы. Нативная стройка автора уже могла быть применена локально до сетевого отказа; central replay/epoch cutover необходим для принятия режима.
4. Общая progression, механизм correctMoney и платежи за посещение станции не исправлены этим PR. Конкуренция cargo/passenger зависит от штатного pathfinding; её нельзя подтвердить mock.
5. Подпись реестра и протокол изменились, смешивание предыдущих билдов запрещено. Save schema не переписана; переход старых identity не объявлен безопасным.

Следующий автономный объём: bounded Lua IPC и offline durable epoch/order component с replay тестами, TCP fault/load bench и ADR для его включения. Автоматически включать центральное исполнение без native/GUI hold адаптации и игровых испытаний нельзя.
