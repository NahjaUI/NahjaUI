#!/usr/bin/env python3
"""Makes the Small / Normal / Big default configurations of NahjaUI from the author's own settings file (Normal).

Usage: make_sizes.py <NahjaUI.json> <DefaultConfiguration.json> <output folder>
Every size, spacing and offset is multiplied by the factor, so the layout keeps its shape (an element that was 40 px right of
its anchor is 52 px right at 1.3). Counts (icons per line, max icons), anchors, colors, rounding and the text size of the menu are not touched."""
import json, re, sys, copy, collections

FACTORS = {"small": 0.8, "big": 1.3}

SIZE = re.compile(r'(Size|SizePx|Width|WidthPx|Height|HeightPx|Spacing|SpacingPx|Gap|GapPx|OffsetX|OffsetY)$')
TEXT = re.compile(r'(TextSize|TimerSize|NameSize|StackTextSize)$|^Size$')   # letter heights: they land on the sizes that exist
SKIP_KEYS = {'MenuTextSize', 'Rounding', 'JobIconStyle', 'IconsPerLine', 'MaxIcons', 'StreamerSeed', 'LastPluginVersion'}
SKIP_TOP = {'Colors'}

stats = collections.Counter()

def scale_value(key, value, k, was_layout):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return value
    if value == 0:
        return value
    new = value * k
    if isinstance(value, float) and value != int(value):
        new = round(new * 2) / 2                      # offsets with "pixel perfect" keep halves
    else:
        new = int(round(new))
    if TEXT.search(key):
        new = max(6, min(72, new))
    elif key.endswith(('Width', 'Height')) and new < 1:
        new = 1
    if isinstance(value, float) and not isinstance(new, float):
        new = float(new)
    return new

def walk(node, k, path):
    if isinstance(node, dict):
        for key in list(node.keys()):
            if key == '$type' or key in SKIP_KEYS or (len(path) == 0 and key in SKIP_TOP):
                continue
            val = node[key]
            if isinstance(val, (dict, list)):
                walk(val, k, path + [key])
            elif SIZE.search(key) or TEXT.search(key):
                new = scale_value(key, val, k, False)
                if new != val:
                    stats[(path[0] if path else '', key)] += 1
                node[key] = new
    elif isinstance(node, list):
        for item in node:
            walk(item, k, path)

def clean(cfg, default):
    out = copy.deepcopy(cfg)
    out.pop('StreamerSeed', None)
    out.pop('SetupPending', None)
    out['SelectedPage'] = 'General'
    out['CollapsedCards'] = []
    for key in ('ShowInServerBar', 'ServerBarIcon', 'ServerBarIconChosen'):
        if key in default:
            out[key] = default[key]
    return out

if __name__ == '__main__':
    config_path, default_path, folder = sys.argv[1:4]
    cfg = json.load(open(config_path, encoding='utf-8'))
    default = json.load(open(default_path, encoding='utf-8'))
    normal = clean(cfg, default)
    json.dump(normal, open(f'{folder}/DefaultConfiguration.normal.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    for name, k in FACTORS.items():
        stats.clear()
        scaled = copy.deepcopy(normal)
        walk(scaled, k, [])
        json.dump(scaled, open(f'{folder}/DefaultConfiguration.{name}.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
        print(name, k, sum(stats.values()), 'values changed')
        for (top, key), n in sorted(stats.items()):
            if top in ('Layouts', 'JobLayouts', 'CommonLayouts') or n > 0:
                pass
        print('  by section:', dict(collections.Counter({t: sum(n for (tt, _), n in stats.items() if tt == t) for t in {t for t, _ in stats}})))
