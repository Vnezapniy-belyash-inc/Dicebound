# LOBBY-UI-01 — приёмка

Дата: 9 октября 2026. Ветка: codex/backlog-stabilization.
Editor и отдельный Windows Development Player: Unity 6000.5.1f1.

## Причина и изменение

После перезагрузки сцены статический кеш GUIStyle содержал уничтоженные Unity-текстуры: ссылки на стиль оставались, а фоны normal/hover/active отсутствовали. Исходное состояние записано в Logs/lobby-ui-before-skin.json.

VttUiSkin проверяет жизнеспособность кешированных фонов, спрайтов и текстур и восстанавливает уничтоженные ресурсы. Созданные общие ресурсы имеют HideAndDontSave и переживают штатную очистку сцены. Изменение кода: коммит 092743b.

## Проверки

- Unity CLI: run_tests --mode editor --filter DiceboundStabilityTests --async_tests true. Результат test_status: 7 passed, 0 failed; Logs/lobby-ui-stability-tests.json. Проверены уничтожение кешированных ресурсов и их восстановление.
- Сборка через Unity CLI build/build_status: Build ID build_bf6721e67297, Succeeded, totalErrors=0; Logs/lobby-ui-build-rerun-result.json. Первая попытка имела две ошибки создания временного ресурса Pipeline и не использована как успешная проверка.
- Editor — хост, отдельный Player Builds/LobbyQA/Dicebound.exe — клиент, локальный транспорт. Три последовательных цикла создания хоста, подключения клиента, загрузки карты, создания токена, настоящего ПКМ через simulate_pointer и завершения сессии через GameNetworkManager.Shutdown.
- В каждом цикле обе стороны вернулись в активное лобби; ID объекта LobbyUI изменился, что подтверждает перезагрузку сцены. Сессия запускалась через API, панели приводились в то же состояние, что обработчиками кнопок; сами клики кнопок создания лобби в этот сценарий не входят.
- Все шесть состояний имеют открытое меню и живые normal/hover/active фоны. ID текстуры неизменен между циклами на каждой стороне: хост -62302, Player -11760. Отчёт: Logs/lobby-three-cycles-result.json; сценарий: Logs/lobby-three-cycles.ps1; ответы CLI: Logs/lobby-cycle-request-*.json.
- Просмотрены все шесть составных кадров: Logs/lobby-cycle-1-host.png, Logs/lobby-cycle-1-player.png, Logs/lobby-cycle-2-host.png, Logs/lobby-cycle-2-player.png, Logs/lobby-cycle-3-host.png, Logs/lobby-cycle-3-player.png. Фон панели виден во всех циклах, кнопки мастера сохраняют фон. Меню игрока без прав редактирования не содержит кнопок мастера.

LOBBY-UI-01 готова. Общая готовность нового backlog: 4/19. Полная регрессия следующего этапа и остальные 15 задач ещё требуется.
