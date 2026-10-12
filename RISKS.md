# Риски и текущие ограничения

Результаты offline tests не подтверждают работу Transport Fever 3. Ни игровые бинарники, ни saves не использовались. Исправленные memory/code-execution risks описаны в STAGE1 docs; ниже остающиеся ограничения.

| Риск | Наблюдение по исходникам/стенду | Что нужно для снятия |
|---|---|---|
| Разные симуляции мира | Каждый ПК рассчитывает мир; не полный authoritative world server | Same build/mods, game census, action-free drift и длительность |
| Premature native apply | Автор мог применить стройку до server verdict | Hold/release/replay автору и engine gate ADR 0001 |
| Save/load boundary | Нет production epoch/frontier transaction и drain всех callbacks | Привязать snapshot к applied frontier; crash/reconnect игровые сценарии |
| Shared progression | Upstream читает прогресс savegame компании | Engine feasibility независимых state/permits; не считать fallback решением |
| Финансы | CorrectMoney, частичный loan/contract routing; Obtain ещё принимает недоказанные terms | Штатный loan_util/API, ledger history/owners/insufficient funds в игре |
| Station billing | Per-stop index не является stable visit ID | Loop/reroute/stop reorder/reload, одна оплата/нулевая сумма transfers |
| Identity | Имя/sid не являются серверным Steam proof; bearer credential только session | Подтверждённый account/initial admission, persistent claim/migration |
| Открытая сеть | TCP не шифруется; наблюдатель может получить credential | Выбор/проверка защищённого транспорта и доверия хосту до публичного использования |
| Недостаточная schema | Whitelist/shape/co policy не знают всю engine Proposal/ref/price структуру | Actual API и native ownership matrix; новые mod events явными schemas |
| Hooks/версии | Native addresses/ABI определены авторами для отдельных builds | Game hash/build gate, byte checks и каждый hook на реальной версии |
| Version ≠ compatibility | Protocol 3 не проверяет все EXE/DLL/mod/game hashes | Fingerprint одинакового пакета и полного engine/mod set |
| Память | 8 x 32 MiB loopback: RSS до ~693 MiB на одном hosted прогоне | Budget production/net48/игры, sustained profiling; меньше копий не равно меньше RSS |
| Размер/скорость снимка | 1 GiB/4096 parts, 600-секундное ожидание sending/loading, bounded queue timeouts | Параметры по настоящим saves/WAN; не обещать Gigantomaniac по этим caps |
| Disk durability | Flush(true)/atomic replace проверены обычными файлами | Power loss/filesystem/game save closure; дисковая ошибка сохраняет old save и pause |
| Journal foundation | Durable input order, но эффекты+frontier не атомарны в игре; компонент не активирован | Engine adapter/async callbacks/quorum по реальному состоянию |
| Lifecycle | Generation checks уменьшают stale callbacks, но не заменяют atomic session fence | Быстрая смена сессии/menu/late callbacks и cancellation в игре |
| Host failure | Нет host migration; credentials в памяти не переживают restart | Recovery последнего согласованного snapshot/journal и confirmed ownership |

Hash снимка обнаруживает повреждение байтов, а не неправильную стройку/грузопотоки/финансовые проводки. Совпадающий hash ошибочного мира не является успехом. Реальный negative test проверяет код компонентов, но injected owner/loan facts не являются измерением двигателя.

Upstream bugs #15–22 и первоначальные доказательства/гипотезы доступны в [историческом аудите](https://github.com/WestDed/TransportFever3-Multiplayer/blob/6611e8f63e29e04aeb978bddd17877b461894edf/RISKS.md). Стабильный release запрещён до TESTING.md/ROADMAP игровых критериев.
