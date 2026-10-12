# Проверка переноса этапа 1 в main

13.10.2026 (Asia/Vladivostok). Заказчик объединил PR #2–#6 через интерфейс GitHub и попросил проверить результат. Автоматическое слияние в main не выполнялось.

## Фактические слияния

| PR | Целевая ветка при слиянии | Merge commit |
|---|---|---|
| #2 | main | 9de017ed0f8cb126b91608d5ac6a8b3ae779a9f3 |
| #3 | fix/offline-tests-safe-decoder | 4b25837356f69351ad8d7345d43f2598e691e6b0 |
| #4 | fix/network-bounds-snapshots | 8d4684dee7e2e413d0ac454fa79234ace7a2bd81 |
| #5 | fix/company-identity-schemas | ae0fb1ae5042346a8cb0b3147d0ac39127a97b59 |
| #6 | fix/offline-order-recovery | d96d3a6cec97c3ec7211841926ecf58d6f25d139 |

Порядок по времени: #2 → #3 → #6 → #5 → #4. PR #3 уже был закрыт, когда в его исходную ветку вошёл #4. Поэтому main содержит только #2. Остальные исправления находятся в fix/network-bounds-snapshots; содержимое не потеряно.

## Восстановленный путь

[PR #7](https://github.com/WestDed/TransportFever3-Multiplayer/pull/7) направлен из `integrate/stage1-reviewed-fixes` прямо в main. Ветка начата с актуальной main 9de017ed. Merge с 8d4684de выполнен без конфликтов и сохраняет историю слияний и исходных commits. Main сама не изменяется этой подготовкой.

Tree main при проверке: `df46fce18acefb4d3cd2467b78f56ce040f6c408`. Trees merge commits #4/#5/#6 и исходного head #6 одинаковы: `1813ca14d7beb649270bb8e1ae570b71e8e6ec73`. Локальный merge в новую ветку также дал этот tree — все файлы совпадают с проверенной версией PR #6.

После merge меняются только Markdown документы, чтобы зафиксировать действительное состояние веток и инструкцию продолжения. Код launcher/mod/native, тесты, build scripts, workflow и LICENSE сохраняются побайтно относительно head #6. Проверка: `git diff --exit-code 683328da4a22a089788219a3159fe07c0756f0b5 HEAD -- launcher mod native tests scripts .github build.bat LICENSE`.

## Проверки и следующая работа

Исходная версия #6 прошла run 38318567426: 67 Python/Lua tests, 276 C# checks на Ubuntu/Windows, 50 LuaLit, full net48 EXE/x64 DLL/package/exports и real TCP generated-file benchmark, все шесть jobs успешны. Для PR #7 результат фиксируется по его итоговому head/run в описании PR. Strict CI и запрет скрывать failures сохраняются.

Заказчику остаётся проверить и объединить **один PR #7 в main**; повторно сливать закрытые #2–#6 не нужно. После слияния проверить актуальную main, начать следующую отдельную ветку от неё и направить новый PR в main. Состояние документа описывает момент подготовки #7; фактическое слияние проверять через GitHub.

TF3 не установлена; игровые бинарники, Steam API и реальные saves не использовались. Интеграция проверенного offline кода не закрывает игровой этап или оставшиеся initial identity/TLS/company ownership/engine permission/cutover задачи.
