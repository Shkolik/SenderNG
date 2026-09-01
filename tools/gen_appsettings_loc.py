#!/usr/bin/env python3
"""Merge App Settings / module localization keys into src/SenderNG.Avalonia/Locale/{en,ru}.json.

Keys resolved by AppSettingsViewModel: appSettings.{global,modules}, the Global leaf
names/descriptions (appSettings.leaf.*) and per-module `module.<id>.name/.description`.
Running this twice is idempotent (existing keys are left alone so hand-edits survive).
"""
import io
import json
import os

BASE = "src/SenderNG/Locale"

# id -> (english, russian)
MODULES = {
    "dro": ("DRO", "УЦИ"),
    "work-parameters": ("Work Parameters", "Рабочие параметры"),
    "program": ("Program", "Программа"),
    "console": ("Console", "Консоль"),
    "grbl-settings": ("GRBL Settings", "Настройки GRBL"),
    "offsets-table": ("Offsets", "Смещения"),
    "app-settings": ("App Settings", "Настройки приложения"),
    "coolant": ("Coolant", "СОЖ"),
    "cutting": ("Cutting", "Резка"),
    "goto": ("Goto", "Переход"),
    "jog": ("Jog", "Ручное перемещение"),
    "macro": ("Macros", "Макросы"),
    "mpos": ("MPos", "Позиция станка"),
    "outline": ("Outline", "Контур"),
    "probing": ("Probing", "Измерение"),
    # NOTE: the Dummy plugin is intentionally absent — 3rd-party plugins serve their
    # own module.<id>.* strings from OutputDir/Modules/<id>/Locale, not the global file.
}

DESC_EN = {
    "dro": "Digital read-out: live axis positions, machine coordinates and spindle feedback.",
    "work-parameters": "Machine & work coordinate systems, G54..G59 switches and the active tool length offset.",
    "program": "Load, view and run G-code programs with per-line status tracking.",
    "console": "Live G-code console: send commands and watch controller responses.",
    "grbl-settings": "View and edit controller settings ($) with a searchable, self-documented tree.",
    "offsets-table": "Edit work offsets (G54..G59) and the tool table, saved back to the controller.",
    "app-settings": "Global application settings, machine mode and per-module configuration.",
    "coolant": "Toggle flood and mist coolant as well as coolant overrides.",
    "cutting": "Feed/spindle overrides and cycle controls for manual cutting.",
    "goto": "Move axes to G28/G30 reference positions or to typed coordinates.",
    "jog": "Manual jog with configurable step distances and speeds.",
    "macro": "Run user-defined macro buttons on demand.",
    "mpos": "Show machine-position and work-position read-outs.",
    "outline": "Outline the loaded program's travel limits and move between them.",
    "probing": "Tool-length, edge, center, rotation and height-map probing cycles.",
}

# Settings-view / generic-field strings per module (id -> {key: (en, ru)})
VIEW_EN = {
    "goto": {
        "module.goto.axisCount": "Goto axis count",
        "module.goto.axisCountHint": "Capped by the connected controller's axis count.",
    },
    "jog": {
        "module.jog.distances": "Distances (mm)",
        "module.jog.speeds": "Speeds (mm/min)",
        "module.jog.step1": "Step 1",
        "module.jog.step2": "Step 2",
        "module.jog.step3": "Step 3",
        "module.jog.speed1": "Speed 1",
        "module.jog.speed2": "Speed 2",
        "module.jog.speed3": "Speed 3",
        "module.jog.diagonal": "Diagonal jogging",
    },
    "offsets-table": {
        "module.offsets-table.showToolTable": "Show tool table",
    },
    "probing": {
        "module.probing.tool-length": "Tool length offset",
        "module.probing.edge-external": "Edge finder, external",
        "module.probing.edge-internal": "Edge finder, internal",
        "module.probing.rotation": "Rotation",
        "module.probing.center": "Center finder",
        "module.probing.heightmap": "Height map",
    },
}

VIEW_RU = {
    "goto": {
        "module.goto.axisCount": "Число осей перехода",
        "module.goto.axisCountHint": "Ограничивается числом осей подключённого контроллера.",
    },
    "jog": {
        "module.jog.distances": "Расстояния (мм)",
        "module.jog.speeds": "Скорости (мм/мин)",
        "module.jog.step1": "Шаг 1",
        "module.jog.step2": "Шаг 2",
        "module.jog.step3": "Шаг 3",
        "module.jog.speed1": "Скорость 1",
        "module.jog.speed2": "Скорость 2",
        "module.jog.speed3": "Скорость 3",
        "module.jog.diagonal": "Диагональные перемещения",
    },
    "offsets-table": {
        "module.offsets-table.showToolTable": "Показать таблицу инструментов",
    },
    "probing": {
        "module.probing.tool-length": "Смещение длины инструмента",
        "module.probing.edge-external": "Поиск кромки, снаружи",
        "module.probing.edge-internal": "Поиск кромки, внутри",
        "module.probing.rotation": "Поворот",
        "module.probing.center": "Поиск центра",
        "module.probing.heightmap": "Карта высот",
    },
}

DESC_RU = {
    "dro": "Цифровой индикатор: живые координаты осей, машинные координаты и обратная связь шпинделя.",
    "work-parameters": "Машинные и рабочие системы координат, переключение G54..G59 и действующее смещение длины инструмента.",
    "program": "Загрузка, просмотр и запуск G-код программ с отслеживанием статуса по строкам.",
    "console": "Живая G-код консоль: отправка команд и просмотр ответов контроллера.",
    "grbl-settings": "Просмотр и редактирование настроек контроллера ($) в поисковой, самодокументированной структуре.",
    "offsets-table": "Редактирование рабочих смещений (G54..G59) и таблицы инструментов с сохранением в контроллер.",
    "app-settings": "Глобальные настройки приложения, режим станка и конфигурация модулей.",
    "coolant": "Переключение СОЖ (флуд/туман) и переопределения.",
    "cutting": "Переопределения подачи/шпинделя и управление циклом для ручной резки.",
    "goto": "Перемещение осей к опорным позициям G28/G30 или по введённым координатам.",
    "jog": "Ручное перемещение с настраиваемыми шагами и скоростями.",
    "macro": "Запуск пользовательских макрокнопок по требованию.",
    "mpos": "Отображение машинных и рабочих координат.",
    "outline": "Контур пределов перемещения загруженной программы и переход между ними.",
    "probing": "Циклы измерения длины инструмента, кромки, центра, поворота и карты высот.",
}

# Common UI labels shared across module widget views.
COMMON_EN = {
    "common.add": "Add",
    "common.remove": "Remove",
    "common.run": "Run",
    "common.go": "Go",
    "common.stop": "Stop",
    "common.start": "Start",
    "common.clear": "Clear",
    "common.save": "Save",
    "common.load": "Load",
    "common.apply": "Apply",
    "common.close": "Close",
    "common.mmMin": "mm/min",
    "appSettings.global": "Global",
    "appSettings.modules": "Modules",
    "appSettings.leaf.mode": "Mode",
    "appSettings.leaf.mode.description": "Machine mode: Mill for CNC milling, Foam for hot-wire foam cutting.",
    "appSettings.leaf.language": "Language",
    "appSettings.leaf.language.description": "Interface language. Applied immediately.",
    "appSettings.enabled": "Enabled",
    "appSettings.alwaysVisible": "Always visible",
    "appSettings.close": "Close",
    "appSettings.selectHint": "Select a setting to edit",
    "appSettings.mode.mill": "Mill",
    "appSettings.mode.foam": "Foam CNC",
}

COMMON_RU = {
    "common.add": "Добавить",
    "common.remove": "Удалить",
    "common.run": "Выполнить",
    "common.go": "Перейти",
    "common.stop": "Стоп",
    "common.start": "Старт",
    "common.clear": "Очистить",
    "common.save": "Сохранить",
    "common.load": "Загрузить",
    "common.apply": "Применить",
    "common.close": "Закрыть",
    "common.mmMin": "мм/мин",
    "appSettings.global": "Глобально",
    "appSettings.modules": "Модули",
    "appSettings.leaf.mode": "Режим",
    "appSettings.leaf.mode.description": "Режим станка: Mill для фрезеровки, Foam для резки пенопласта горячей нитью.",
    "appSettings.leaf.language": "Язык",
    "appSettings.leaf.language.description": "Язык интерфейса. Применяется сразу.",
    "appSettings.enabled": "Включено",
    "appSettings.alwaysVisible": "Всегда виден",
    "appSettings.close": "Закрыть",
    "appSettings.selectHint": "Выберите настройку для редактирования",
    "appSettings.mode.mill": "Фрезеровка",
    "appSettings.mode.foam": "Резка пенопласта",
}


# Widget-view + ViewModel status strings per module (key -> (en, ru)).
WIDGET = {
    "goto": {
        "module.goto.referencePoints": ("Reference points", "Опорные точки"),
        "module.goto.home": ("G28 · Home", "G28 · Домой"),
        "module.goto.ref2": ("G30 · Ref. 2", "G30 · Ист. 2"),
        "module.goto.setHome": ("Set home (G28.1)", "Установить дом (G28.1)"),
        "module.goto.setRef2": ("Set ref. 2 (G30.1)", "Установить ист. 2 (G30.1)"),
        "module.goto.position": ("Goto position (machine coords)", "Перейти к позиции (машинные координаты)"),
        "module.goto.homeFirst": ("Home the machine first ($H)", "Сначала выполните возврат домой ($H)"),
        "module.goto.goingHome": ("Going to home position", "Переход к домашней позиции"),
        "module.goto.goingRef2": ("Going to second reference point", "Переход ко второй опорной точке"),
        "module.goto.homeRecorded": ("Home position recorded", "Домашняя позиция записана"),
        "module.goto.ref2Recorded": ("Second reference point recorded", "Вторая опорная точка записана"),
        "module.goto.goingPosition": ("Going to position", "Переход к позиции"),
        "module.goto.invalidValue": ("Invalid value: {0}", "Неверное значение: {0}"),
        "module.goto.enterCoordinate": ("Enter at least one coordinate", "Введите хотя бы одну координату"),
        "module.goto.commandFailed": ("{0} failed: {1}", "{0} не выполнено: {1}"),
    },
    "jog": {
        "module.jog.distance": ("Distance (mm)", "Расстояние (мм)"),
        "module.jog.speed": ("Speed (mm/min)", "Скорость (мм/мин)"),
        "module.jog.feedHold": ("Feed hold", "Пауза подачи"),
        "module.jog.syncGantries": ("Synchronize both gantries", "Синхронизировать обе каретки"),
    },
    "dro": {
        "module.dro.zeroAxis": ("Zero axis", "Обнулить ось"),
        "module.dro.zeroAll": ("Zero all axes", "Обнулить все оси"),
        "module.dro.zeroed": ("All axes zeroed", "Все оси обнулены"),
        "module.dro.zeroedAxis": ("{0} zeroed", "Ось {0} обнулена"),
        "module.dro.zeroAxisFailed": ("Zero {0} failed: {1}", "Не удалось обнулить ось {0}: {1}"),
        "module.dro.zeroAllFailed": ("Zero all failed: {0}", "Не удалось обнулить все оси: {0}"),
    },
    "work-parameters": {
        "module.work-parameters.offset": ("Offset", "Смещение"),
        "module.work-parameters.tool": ("Tool", "Инструмент"),
        "module.work-parameters.tloRefd": ("TLO ref'd", "TLO установлено"),
        "module.work-parameters.tloActive": ("TLO active", "TLO активно"),
    },
    "program": {
        "module.program.line": ("Line", "Строка"),
        "module.program.data": ("Data", "Данные"),
    },
    "console": {
        "module.console.verbose": ("Verbose", "Подробный вывод"),
        "module.console.filterRealtime": ("Filter out realtime responses", "Скрыть ответы реального времени"),
        "module.console.showAllRealtime": ("Show all realtime responses", "Показывать все ответы реального времени"),
    },
    "offsets-table": {
        "module.offsets-table.offset": ("Offset", "Смещение"),
        "module.offsets-table.tool": ("Tool", "Инструмент"),
        "module.offsets-table.radius": ("Radius", "Радиус"),
        "module.offsets-table.selectedOffset": ("Selected offset:", "Выбранное смещение:"),
        "module.offsets-table.getCurrentPosition": ("Get current position", "Получить текущую позицию"),
        "module.offsets-table.clearAll": ("Clear all", "Очистить всё"),
        "module.offsets-table.setAll": ("Set all", "Установить всё"),
        "module.offsets-table.refreshed": ("Offsets refreshed", "Смещения обновлены"),
        "module.offsets-table.notWritable": ("Only G54..G59.3 are writable", "Доступны только G54..G59.3"),
        "module.offsets-table.invalidAxisValue": ("Invalid numeric value at axis {0}", "Неверное числовое значение по оси {0}"),
        "module.offsets-table.set": ("Set {0}", "Установлено {0}"),
        "module.offsets-table.setFailed": ("Set {0} failed: {1}", "Сбой установки {0}: {1}"),
        "module.offsets-table.inAlarm": ("Controller is in alarm — unlock or home first", "Контроллер в аварии — разблокируйте или вернитесь домой"),
        "module.offsets-table.copied": ("Current position copied to {0}", "Текущая позиция скопирована в {0}"),
        "module.offsets-table.cleared": ("All offsets cleared in editor; press Set all to write them", "Все смещения очищены в редакторе; нажмите «Установить всё» для записи"),
        "module.offsets-table.saved": ("Saved {0} offsets", "Сохранено смещений: {0}"),
        "module.offsets-table.savedWithFailures": ("Saved {0} offsets; {1} failed", "Сохранено смещений: {0}; неудачных: {1}"),
        "module.offsets-table.noToolTable": ("This controller has no G10 L1 tool table (classic grbl 1.1)", "Этот контроллер не имеет таблицы инструментов G10 L1 (классический grbl 1.1)"),
        "module.offsets-table.invalidToolValue": ("Invalid tool-table value", "Неверное значение таблицы инструментов"),
        "module.offsets-table.noToolChanges": ("No tool changes to save", "Нет изменений инструментов для сохранения"),
        "module.offsets-table.savedTools": ("Saved {0} changed tools", "Сохранено изменённых инструментов: {0}"),
        "module.offsets-table.savedToolFailures": ("Saved {0} tools; {1} failed", "Сохранено инструментов: {0}; неудачных: {1}"),
        "module.offsets-table.toolName": ("Tool", "Инструмент"),
        "module.offsets-table.visibleTools": ("Visible tools", "Видимые инструменты"),
        "module.offsets-table.toolsCleared": ("All tools cleared in editor; press Set all to write them", "Все инструменты очищены в редакторе; нажмите «Установить всё» для записи"),
        "module.offsets-table.touchOffCopied": ("Machine position copied to T{0} — review and press Set all", "Позиция станка скопирована в T{0} — проверьте и нажмите «Установить всё»"),
        "module.offsets-table.touchOffCopiedWork": ("Work position copied to T{0} (machine position unavailable) — review and press Set all", "Рабочая позиция скопирована в T{0} (позиция станка недоступна) — проверьте и нажмите «Установить всё»"),
        "module.offsets-table.touchOffNone": ("Touch-off applies to a numbered tool only", "Касание применимо только к пронумерованному инструменту"),
    },
    "coolant": {
        "module.coolant.flood": ("Flood", "Полив"),
        "module.coolant.mist": ("Mist", "Туман"),
    },
    "cutting": {
        "module.cutting.feed": ("Feed", "Подача"),
        "module.cutting.speed": ("Speed", "Скорость"),
        "module.cutting.feedOverride": ("Feed override", "Переопределение подачи"),
        "module.cutting.rapidsOverride": ("Rapids override", "Переопределение быстрых"),
        "module.cutting.speedOverride": ("Speed override", "Переопределение скорости"),
        "module.cutting.reset100": ("Reset to 100%", "Сброс до 100%"),
        "module.cutting.spindleRpmHint": ("Spindle speed (RPM) used by CW/CCW", "Скорость шпинделя (об/мин) для CW/CCW"),
        "module.cutting.cw": ("CW", "CW"),
        "module.cutting.ccw": ("CCW", "CCW"),
        "module.cutting.off": ("Off", "Выкл"),
        "module.cutting.spindleCwHint": ("Spindle on, clockwise (M3)", "Шпиндель вкл, по часовой (M3)"),
        "module.cutting.spindleCcwHint": ("Spindle on, counter-clockwise (M4)", "Шпиндель вкл, против часовой (M4)"),
        "module.cutting.spindleOffHint": ("Spindle off (M5)", "Шпиндель выкл (M5)"),
    },
    "macro": {
        "module.macro.namePlaceholder": ("Macro name", "Имя макроса"),
        "module.macro.codePlaceholder": ("G-code lines, one per line", "Строки G-кода, по одной в строке"),
        "module.macro.newMacro": ("Macro {0}", "Макрос {0}"),
    },
    "outline": {
        "module.outline.description": ("Moves the tool around the X/Y outline of the loaded program's limits.",
                                        "Перемещает инструмент по контуру X/Y пределов загруженной программы."),
        "module.outline.warning": ("Ensure the tool tip is clear of any obstructions!",
                                    "Убедитесь, что путь инструмента свободен от препятствий!"),
        "module.outline.feedRate": ("Feed rate:", "Подача:"),
        "module.outline.sent": ("Outline sent", "Контур отправлен"),
        "module.outline.noXyLimits": ("Outline: program has no X/Y limits", "Контур: программа без пределов X/Y"),
        "module.outline.failed": ("Outline failed: {0}", "Ошибка контура: {0}"),
    },
    "grbl-settings": {
        "module.grbl-settings.load": ("Load", "Загрузить"),
        "module.grbl-settings.saveChanges": ("Save changes", "Сохранить изменения"),
        "module.grbl-settings.searchPlaceholder": ("Search settings...", "Поиск настроек..."),
        "module.grbl-settings.selectHint": ("Select a setting to edit", "Выберите настройку для редактирования"),
        "module.grbl-settings.enabled": ("Enabled", "Включено"),
        "module.grbl-settings.descriptionLabel": ("Description", "Описание"),
        "module.grbl-settings.saveHint": ("Save writes only the changed settings.", "Сохранить записывает только изменённые настройки."),
        "module.grbl-settings.editing": ("Editing {0}", "Редактирование {0}"),
        "module.grbl-settings.loaded": ("Loaded {0} settings", "Загружено настроек: {0}"),
        "module.grbl-settings.loadFailed": ("Load failed: {0}", "Ошибка загрузки: {0}"),
        "module.grbl-settings.noChanges": ("No changes", "Нет изменений"),
        "module.grbl-settings.savedOne": ("Saved 1 setting", "Сохранена 1 настройка"),
        "module.grbl-settings.savedMany": ("Saved {0} settings", "Сохранено настроек: {0}"),
        "module.grbl-settings.saveFailed": ("Save failed: {0}", "Ошибка сохранения: {0}"),
    },
    "probing": {
        "module.probing.profileActions": ("Profile actions", "Действия с профилем"),
        "module.probing.probeTipTool": ("Probe tip/tool", "Щуп инструмента"),
        "module.probing.diameter": ("Diameter:", "Диаметр:"),
        "module.probing.touchPlateHeight": ("Touch plate/fixture height", "Высота пластины/приспособления"),
        "module.probing.touchPlate": ("Touch plate:", "Пластина:"),
        "module.probing.fixture": ("Fixture:", "Приспособление:"),
        "module.probing.distancesAndSpeed": ("Probing distances and speed", "Расстояния и скорость измерения"),
        "module.probing.rapidsFeedRate": ("Rapids feed rate:", "Подача быстрых перемещений:"),
        "module.probing.rapidsFeedRateHint": ("Feed for rapid moves; 0 uses the controller setting", "Подача быстрых перемещений; 0 — настройка контроллера"),
        "module.probing.searchFeedRateHint": ("Feed rate for the initial probing move", "Подача начального движения измерения"),
        "module.probing.latchFeedRateHint": ("Feed rate for the second slow probing move", "Подача второго медленного движения измерения"),
        "module.probing.probingDistanceHint": ("Maximum distance of a probing move", "Максимальная дистанция движения измерения"),
        "module.probing.latchDistanceHint": ("Retract before the slow probe; 0 skips it", "Отвод перед медленным зондом; 0 — пропустить"),
        "module.probing.xyClearanceHint": ("Initial distance away from the face to probe", "Начальное расстояние от поверхности для измерения"),
        "module.probing.offsetHint": ("Distance from the corner to the probe point", "Расстояние от угла до точки измерения"),
        "module.probing.depthHint": ("Z below the start position where XY probing happens", "Z ниже стартовой позиции, где происходит XY-измерение"),
        "module.probing.xOffsetHint": ("X offset from the probe to the controlled point", "Смещение X от щупа до управляемой точки"),
        "module.probing.yOffsetHint": ("Y offset from the probe to the controlled point", "Смещение Y от щупа до управляемой точки"),
        "module.probing.searchFeedRate": ("Search feed rate:", "Подача поиска:"),
        "module.probing.latchFeedRate": ("Latch feed rate:", "Подача медленного хода:"),
        "module.probing.probingDistance": ("Probing distance:", "Дистанция измерения:"),
        "module.probing.latchDistance": ("Latch distance:", "Дистанция медленного хода:"),
        "module.probing.clearances": ("Probing clearances", "Зазоры измерения"),
        "module.probing.xyClearance": ("XY Clearance:", "Зазор XY:"),
        "module.probing.offset": ("Offset:", "Смещение:"),
        "module.probing.depth": ("Depth:", "Глубина:"),
        "module.probing.xyOffsets": ("Probe/touchplate XY offsets", "Смещения XY щупа/пластины"),
        "module.probing.isTouchPlate": ("Is touch plate", "Это пластина"),
        "module.probing.setOffset": ("Set offset", "Установить смещение"),
        "module.probing.measure": ("Measure", "Измерить"),
        "module.probing.system": ("System:", "Система:"),
        "module.probing.tipTool": ("Tool length", "Длина инструмента"),
        "module.probing.edgeExternal": ("Edge external", "Снаружи по кромке"),
        "module.probing.edgeInternal": ("Edge internal", "Внутри по кромке"),
        "module.probing.tabRotation": ("Rotation", "Поворот"),
        "module.probing.tabCenter": ("Center", "Центр"),
        "module.probing.warning": ("Warning! Use with care - incorrect parameters may damage your probe!",
                                    "Внимание! Будьте осторожны — неверные параметры могут повредить щуп!"),
        # Tool length panel
        "module.probing.toolLengthDesc": ("Probe Z downward onto the touch plate / fixture to establish the tool length offset.",
                                            "Проведите Z вниз до пластины/приспособления, чтобы установить смещение длины инструмента."),
        "module.probing.probeFixture": ("Probe fixture @ G59.3", "Измерить приспособление @ G59.3"),
        "module.probing.establishReference": ("Establish reference offset", "Установить опорное смещение"),
        "module.probing.withTool": ("with tool:", "инструментом:"),
        "module.probing.storeProbed": ("store probed length into the tool table (grblHAL)", "сохранить измеренную длину в таблицу инструментов (grblHAL)"),
        "module.probing.setCoordSystem": ("Set coordinate system or offset", "Установить систему координат или смещение"),
        "module.probing.fixtureProbing": ("Fixture probing rapids to the fixture origin (0,0) and probes from there;",
                                            "Измерение приспособления выполняется быстрым ходом к началу (0,0) и далее оттуда;"),
        "module.probing.workpieceHeight": ("Workpiece height:", "Высота заготовки:"),
        # Edge finder panel
        "module.probing.probeZ": ("Probe Z", "Измерить Z"),
        "module.probing.edgeOffset": ("Edge offset:", "Смещение кромки:"),
        "module.probing.edgeOffsetHint": ("0 uses half the probe diameter", "0 — половина диаметра щупа"),
        # Center finder panel
        "module.probing.inside": ("Inside", "Внутри"),
        "module.probing.outside": ("Outside", "Снаружи"),
        "module.probing.dimensions": ("Workpiece dimensions", "Размеры заготовки"),
        "module.probing.xSize": ("X size:", "Размер X:"),
        "module.probing.ySize": ("Y size:", "Размер Y:"),
        "module.probing.lockHint": ("Locked: X and Y sizes stay equal. Unlock to probe in one direction only.",
                                      "Закрыто: размеры X и Y остаются равными. Откройте, чтобы измерить по одному направлению."),
        "module.probing.passes": ("Passes:", "Проходы:"),
        "module.probing.passesHint": ("More than one pass is usually needed for accurate dimensions",
                                        "Для точных размеров обычно нужно более одного прохода"),
        # Rotation panel
        "module.probing.rotationDesc": ("Probe two points along an edge to measure the angle of the workpiece.",
                                          "Измерьте две точки вдоль кромки, чтобы определить угол заготовки."),
        "module.probing.edge": ("Edge:", "Кромка:"),
        "module.probing.applyRotation": ("Apply rotation to loaded program", "Применить поворот к загруженной программе"),
        "module.probing.applyRotationHint": ("Rotates the loaded program's X/Y coordinates by the probed angle",
                                                "Поворачивает координаты X/Y загруженной программы на измеренный угол"),
        # Height map panel
        "module.probing.heightmapDesc": ("Probe a Z grid over the area. The map is stored relative to the first probed point.",
                                           "Измерьте Z-сетку по области. Карта хранится относительно первой измеренной точки."),
        "module.probing.area": ("Area", "Область"),
        "module.probing.minX": ("Min X:", "Мин X:"),
        "module.probing.maxX": ("Max X:", "Макс X:"),
        "module.probing.minY": ("Min Y:", "Мин Y:"),
        "module.probing.maxY": ("Max Y:", "Макс Y:"),
        "module.probing.grid": ("Grid", "Сетка"),
        "module.probing.gridX": ("Grid X:", "Сетка X:"),
        "module.probing.gridY": ("Grid Y:", "Сетка Y:"),
        "module.probing.lockXY": ("Lock X/Y grid size", "Закрепить размер сетки X/Y"),
        "module.probing.addPause": ("Add pause between points", "Пауза между точками"),
        "module.probing.addPauseHint": ("Wait for cycle start between probe points", "Ждать пуска цикла между точками измерения"),
        "module.probing.setToolOffset": ("Set tool offset after probing", "Установить смещение инструмента после измерения"),
        "module.probing.saveMap": ("Save map", "Сохранить карту"),
        "module.probing.loadMap": ("Load map", "Загрузить карту"),
        "module.probing.applyToProgram": ("Apply to program", "Применить к программе"),
        # ProbeViewModel status / result strings
        "module.probing.instr.toolLength": ("Probe the touch plate/fixture to set the tool length offset.",
                                              "Измерьте пластину/приспособление, чтобы установить смещение длины инструмента."),
        "module.probing.instr.edge": ("Click an edge, corner or Z in the diagram to select the probing action.\nMove the probe above the indicated position before Start.",
                                        "Выберите кромку, угол или Z на схеме.\nПеред стартом поместите щуп над указанной позицией."),
        "module.probing.instr.center": ("Place the probe above the approximate center of the workpiece before Start.\nSelect inside or outside probing first.",
                                          "Поместите щуп над приблизительным центром заготовки.\nСначала выберите измерение внутри или снаружи."),
        "module.probing.instr.rotation": ("Select an edge to probe two points along, then Start to measure the angle.",
                                            "Выберите кромку для измерения двух точек, затем Старт для определения угла."),
        "module.probing.instr.heightmap": ("Set the map area and grid size, then Start to probe the grid.",
                                             "Задайте область и размер сетки, затем Старт для измерения сетки."),
        "module.probing.notConnected": ("Not connected - connect first (or use Demo mode).", "Не подключено — сначала подключитесь (или используйте режим Demo)."),
        "module.probing.cancelled": ("Probing cancelled", "Измерение отменено"),
        "module.probing.failed": ("Probing failed: {0}", "Ошибка измерения: {0}"),
        "module.probing.noPosition": ("Probing failed - no probe position captured", "Ошибка измерения — позиция щупа не получена"),
        "module.probing.toolLengthSet": ("Tool length offset set: {0}", "Смещение длины инструмента установлено: {0}"),
        "module.probing.measuredZ": ("Measured Z: {0}", "Измерен Z: {0}"),
        "module.probing.toolLengthStored": ("Tool length offset stored to T{0}: {1}", "Смещение длины инструмента сохранено в T{0}: {1}"),
        "module.probing.selectEdgeFirst": ("Select an edge or corner first", "Сначала выберите кромку или угол"),
        "module.probing.measured": ("Measured: X{0} Y{1} Z{2}", "Измерено: X{0} Y{1} Z{2}"),
        "module.probing.probingCompleted": ("Probing completed: {0}", "Измерение завершено: {0}"),
        "module.probing.selectCenterFirst": ("Select inside or outside probing first", "Сначала выберите измерение внутри или снаружи"),
        "module.probing.sizeMustBeSet": ("Workpiece size must be set", "Необходимо задать размер заготовки"),
        "module.probing.notEnoughPos": ("Probing failed - not enough probe positions", "Ошибка измерения — недостаточно позиций щупа"),
        "module.probing.xDistance": ("Probing completed: X distance {0}, Y distance {1}", "Измерение завершено: расстояние X {0}, Y {1}"),
        "module.probing.xDistanceArrow": ("Probing completed: X distance {0}, Y distance {1} -> {2}", "Измерение завершено: расстояние X {0}, Y {1} -> {2}"),
        "module.probing.selectRotationEdge": ("Select an edge for rotation probing", "Выберите кромку для измерения поворота"),
        "module.probing.twoPosRequired": ("Probing failed - two probe positions required", "Ошибка измерения — нужны две позиции щупа"),
        "module.probing.probedAngle": ("Probed angle: {0}°", "Угол измерения: {0}°"),
        "module.probing.zRange": ("Probing completed: Z range {0} to {1}", "Измерение завершено: диапазон Z от {0} до {1}"),
        "module.probing.expectedFail": ("Probing failed - expected {0} positions, got {1}", "Ошибка измерения — ожидалось {0} позиций, получено {1}"),
        "module.probing.noHeightMap": ("No height map to save", "Нет карты высот для сохранения"),
        "module.probing.heightMapSaved": ("Height map saved to {0}", "Карта высот сохранена: {0}"),
        "module.probing.saveFailed": ("Save failed: {0}", "Ошибка сохранения: {0}"),
        "module.probing.noHeightMapAt": ("No height map at {0}", "Нет карты высот: {0}"),
        "module.probing.heightMapLoaded": ("Height map loaded from {0}", "Карта высот загружена: {0}"),
        "module.probing.loadFailed": ("Load failed: {0}", "Ошибка загрузки: {0}"),
        "module.probing.probeOrLoadFirst": ("Probe or load a height map first", "Сначала измерьте или загрузите карту высот"),
        "module.probing.noProgramLoaded": ("No program loaded", "Программа не загружена"),
        "module.probing.heightMapApplied": ("Height map applied to program", "Карта высот применена к программе"),
        "module.probing.probeAngleFirst": ("Probe an edge angle first", "Сначала измерьте угол кромки"),
        "module.probing.programRotated": ("Program rotated", "Программа повёрнута"),
    },
}


def build(lang):
    is_en = lang == "en"
    common = COMMON_EN if is_en else COMMON_RU
    descs = DESC_EN if is_en else DESC_RU
    views = VIEW_EN if is_en else VIEW_RU
    strings = dict(common)
    for mid, (en_name, ru_name) in MODULES.items():
        name = en_name if is_en else ru_name
        strings[f"module.{mid}.name"] = name
        strings[f"module.{mid}.description"] = descs[mid]
    for mid, entries in views.items():
        for key, value in entries.items():
            strings[key] = value
    for mid, entries in WIDGET.items():
        for key, (en_value, ru_value) in entries.items():
            strings[key] = en_value if is_en else ru_value
    return strings


def merge(path, lang):
    with io.open(path, encoding="utf-8") as f:
        data = json.load(f)
    data.setdefault("strings", {}).update(build(lang))
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print("updated", path)


merge(os.path.join(BASE, "en.json"), "en")
merge(os.path.join(BASE, "ru.json"), "ru")