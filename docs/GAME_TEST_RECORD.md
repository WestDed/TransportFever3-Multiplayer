# Протокол игрового испытания — заполняется после приобретения TF3

Статус: NOT RUN. Этот файл — шаблон, не свидетельство выполненного теста.

- Дата/проверяющий/scenario и критерий из TESTING.md:
- Game build/hash; EXE/DLL/mod/DLC/package/branch/commit hashes:
- Source save/hash/seed/map; отдельная неизменённая копия:
- Host/client hardware/OS, roles/companies, network/latency/bandwidth:
- Начальные balances/loan histories/owners/geometry/stocks/onboard:
- Действия по шагам, concurrency/pause/speed, epoch/sequence/uid:
- Expected result/события/callbacks/плательщик/число применений:
- Actual result/ошибка; snapshot/census до/после; logs:
- Resync count/time, corrective money counters, CPU/RAM/step metrics:
- Save/reload/reconnect behaviour и повторяемость:
- Итог PASS/FAIL/BLOCKED, основание и следующая проверка:

Hash совпал — проверить семантику. Если неизвестен правильный результат/не выполнен callback/отсутствует история платежей, не ставить PASS. При crash/несогласованности сохранить логи и source save, продолжать на копии.
