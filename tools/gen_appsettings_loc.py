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
    "dummy": ("Dummy", "Заглушка"),
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
    "dummy": "Example third-party plugin used to prove assembly-scanned module discovery.",
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
    "dummy": "Пример стороннего плагина для проверки обнаружения модулей по сборкам.",
}

COMMON_EN = {
    "appSettings.global": "Global",
    "appSettings.modules": "Modules",
    "appSettings.leaf.mode": "Mode",
    "appSettings.leaf.mode.description": "Machine mode: Mill for CNC milling, Foam for hot-wire foam cutting.",
    "appSettings.leaf.language": "Language",
    "appSettings.leaf.language.description": "Interface language. Applied immediately.",
}

COMMON_RU = {
    "appSettings.global": "Глобально",
    "appSettings.modules": "Модули",
    "appSettings.leaf.mode": "Режим",
    "appSettings.leaf.mode.description": "Режим станка: Mill для фрезеровки, Foam для резки пенопласта горячей нитью.",
    "appSettings.leaf.language": "Язык",
    "appSettings.leaf.language.description": "Язык интерфейса. Применяется сразу.",
}


def build(lang):
    is_en = lang == "en"
    common = COMMON_EN if is_en else COMMON_RU
    descs = DESC_EN if is_en else DESC_RU
    strings = dict(common)
    for mid, (en_name, ru_name) in MODULES.items():
        name = en_name if is_en else ru_name
        strings[f"module.{mid}.name"] = name
        strings[f"module.{mid}.description"] = descs[mid]
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