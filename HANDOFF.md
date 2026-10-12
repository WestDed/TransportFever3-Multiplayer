# Продолжение работы после аудита

Продолжение: [компании и протокол, PR #4](docs/STAGE1_COMPANIES_PROTOCOL.md). AUD-04 исправлен; reconnect credential, client schema, host-reported company binding и Repay terms добавлены. Steam authentication/engine ownership/central replay пока не приняты; ниже исторические состояния и предыдущие этапы.

PR #3 (`fix/network-bounds-snapshots`) зависит от #2, оба без автоматического merge. Прочитать [STAGE1_NETWORK_SNAPSHOTS](docs/STAGE1_NETWORK_SNAPSHOTS.md): bounded reads/outboxes, streamed SHA256 snapshots и full Windows build уже добавлены. Продолжать company identity/schema и epoch/order, не переделывать выполненную работу. Фактический финальный CI смотреть по head SHA PR.

Актуальное продолжение: [PR #2](https://github.com/WestDed/TransportFever3-Multiplayer/pull/2), ветка `fix/offline-tests-safe-decoder` от main `6611e8f`. Сначала прочитать [STAGE1_TESTS_DECODER](docs/STAGE1_TESTS_DECODER.md): mock loader и декодеры уже исправлены, повторять эти работы не нужно. AUD-04, авторизация компаний, command schema, ограничения чтения, порядок и эпохи — следующий объём. Ниже сохранена информация о первичной публикации аудита.


Основной проект: https://github.com/WestDed/TransportFever3-Multiplayer
Локальная ветка: `audit/separate-companies-2026-10-12`, база `0c4a6d35bb5fd22f2ef851d8089e4c6f2cde6773`.

При первой попытке 12.10.2026 GitHub connector вернул HTTP 403 `Resource not accessible by integration`; обычный HTTPS git также не имел credentials. После переподключения GitHub пользователем доступ восстановлен: установка приложения видна для WestDed и ветка создана в основном репозитории. Результаты публикуются через GitHub connector в эту ветку, main не изменяется напрямую. Первичный архив содержит локальный коммит `774881808fdd69f100ffa7ddb199ab3c9ce7e008`; коммит публикации может иметь иной SHA при том же содержимом.

Для следующей сессии:
1. Открыть PR ветки `audit/separate-companies-2026-10-12`, прочитать пять документов и посмотреть его фактические проверки Actions.
2. Сверить main/HEAD и существующие изменения, не перезаписывать чужую работу.
3. Следовать этапу 1 ROADMAP.md: восстановить mock loader, ограничить декодер/протокол, затем порядок и эпохи команд. Игровые функции принимаются только по настоящим испытаниям после приобретения TF3.
4. Каждое продолжение — отдельная ветка и PR с результатами проверок. Разрешение пользователя на публикацию аудита уже дано; merge не выполняется автоматически.

## Текст Pull Request

Название: `Технический аудит отдельных компаний: архитектура, риски и проверки без игры`

В существующей копии MPFever 0.3.6 отдельные компании уже есть, но прогресс общий, сетевой хост в основном ретранслирует команды, а восстановление может закреплять неверно повторённое действие. Этот PR фиксирует архитектуру и ограничения и обосновывает продолжение MPFever вместо переписывания с нуля.

Добавлены ARCHITECTURE.md, FEATURES.md, ROADMAP.md, RISKS.md, TESTING.md; 16 проверок реальных чистых Lua-компонентов и offline workflow. Сохранены исходные лицензия и авторство; производственный Lua/C#/native не изменён. Альтернатива Juliansgith сопоставлена по закреплённому SHA, включая различие между проектным описанием и native AcceptAll ruleset.

Проверки: 14 PASS, 2 expected failures (исполняемый payload в decoder и sid отсутствует в registry signature). Исходный test_lang.py FAIL: три новых ключа отсутствуют в 11 локалях. Исходный test_lockstep.py FAIL при загрузке отсутствующего mock namespace-модуля. Workflow отдельно показывает эти baseline failures и сохраняет логи; зелёный pure-Lua job не свидетельствует об их исправлении.

TF3 не установлена. Игровые, native Windows и альтернативные Rust-тесты не выполнялись. Open issues #15–22 — отчёты upstream, не наши воспроизведения. Следующий этап: восстановить harness, ограничить парсер/протокол, проверить порядок/эпохи команд; после покупки игры — regressions строительства, затем независимость компаний и конкуренция.
