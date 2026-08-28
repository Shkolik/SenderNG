import json
import os

hal = "hal-dump"
TYPES = {0: "bool", 1: "bitfield", 2: "xbitfield", 3: "radio", 4: "axismask", 5: "integer", 6: "float", 7: "text", 8: "password", 9: "ip4"}


def num(x):
    x = x.strip()
    if x == "":
        return None
    try:
        v = float(x)
        return int(v) if v == int(v) else v
    except ValueError:
        return None


def parse_es(path):
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.strip()
        if not line.startswith("[SETTING:"):
            continue
        body = line[len("[SETTING:"):-1]
        f = body.split("|")
        if len(f) < 10:
            continue
        t = f[4].strip()
        out[int(f[0])] = {
            "id": int(f[0]),
            "group": int(f[1]),
            "name": f[2],
            "unit": f[3],
            "type": TYPES.get(int(t) if t else 7, "text"),
            "format": f[5],
            "min": num(f[6]),
            "max": num(f[7]),
            "reboot": f[8] == "1",
            "allowNull": f[9] == "1",
        }
    return out


def parse_groups(path):
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.strip()
        if not line.startswith("[SETTINGGROUP:"):
            continue
        body = line[len("[SETTINGGROUP:"):-1]
        f = body.split("|")
        if len(f) == 3:
            out[int(f[0])] = {"id": int(f[0]), "parent": int(f[1]), "name": f[2]}
    return out


def parse_sed(path):
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.strip()
        if not line.startswith("[SETTINGDESCR:"):
            continue
        body = line[len("[SETTINGDESCR:"):-1]
        idx = body.find("|")
        if idx < 0:
            continue
        try:
            sid = int(body[:idx])
        except ValueError:
            continue
        # firmware sends literal backslash-n; convert to real newlines
        desc = body[idx + 1:].replace("\\n", "\n").strip()
        out[sid] = desc
    return out


settings = parse_es(os.path.join(hal, "COM9_ES.txt"))
groups = parse_groups(os.path.join(hal, "COM9_EG.txt"))
descs = parse_sed(os.path.join(hal, "COM9_SED_all.txt"))

for sid, desc in descs.items():
    if sid in settings:
        settings[sid]["description"] = desc

catalog = {
    "locale": "en",
    "strings": {
        "grblSettings.title": "GRBL settings",
        "grblSettings.load": "Load",
        "grblSettings.save": "Save changes",
        "grblSettings.reload": "Reload",
        "grblSettings.searchPlaceholder": "Search…",
        "grblSettings.value": "Value",
        "grblSettings.loaded": "Loaded {count} settings",
        "grblSettings.noChanges": "No changes",
        "grblSettings.saved": "Saved {count} setting(s)",
        "grblSettings.loadFailed": "Load failed: {error}",
        "grblSettings.saveFailed": "Save failed: {error}",
        "grblSettings.desc.none": "No description available.",
    },
    "grblSettingGroups": groups,
    "grblSettings": settings,
}

out = "src/SenderNG.Core/Localization/en.json"
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, "w", encoding="utf-8") as f:
    json.dump(catalog, f, indent=2, ensure_ascii=False)
print("wrote", out, "-", len(settings), "settings,", len(groups), "groups,", len(descs), "descriptions")
