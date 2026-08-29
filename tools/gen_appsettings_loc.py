#!/usr/bin/env python3
"""Merge App Settings / module localization keys into src/SenderNG.Avalonia/Locale/{en,ru}.json.

Keys resolved by AppSettingsViewModel: appSettings.{global,modules}, the Global leaf
names/descriptions (appSettings.leaf.*) and per-module `module.<id>.name/.description`.
Running this twice is idempotent (existing keys are left alone so hand-edits survive).
"""
import io
import json
import os

BASE = "src/SenderNG.Avalonia/Locale"

# id -> (english, russian)
MODULES = {
    "dro": ("DRO", "DRO"),
    "work-parameters": ("Work Parameters", "Рабочие параметры"),
    "program": ("Program", "Программа"),
    "console": ("Console", "Консоль"),
    "grbl-settings": ("GRBL Settings", "Настройки GRBL"),
    "offsets-table": ("Offsets", "Смещения"),
    "app-settings": ("App Settings", "Настройки приложения"),
    "coolant": ("Coolant", "СОЖ"),
    "cutting": ("Cutting", "Резка"),
    "goto": ("Goto", "Переход"),
    "jog": ("Jog", "Джог"),
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

COMMON_EN = {
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