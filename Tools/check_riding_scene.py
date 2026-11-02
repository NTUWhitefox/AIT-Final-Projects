"""Check the actual saved wheel hierarchy; --repair reparents only affected parts.

Uses the real C# classifier output from check_riding_code.py. Preserves existing
world translations and every other scene property. For a non-identity rotated or
scaled ancestor, abort rather than guessing: the runtime repair can handle it.
"""
import argparse
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "VR-Motor-Street-Riding"
SCENE = PROJECT / "Assets/Scenes/FirstRideTest.unity"
OUTPUT = PROJECT / "Personal/CodeChecks"


def check_scene(repair=False):
    original = SCENE.read_text(encoding="utf-8")
    header = original[:original.index("--- !u!")]
    documents = {}
    for match in re.finditer(r'--- !u!(\d+) &(\d+)\n(.*?)(?=--- !u!|\Z)', original, re.S):
        kind, key, body = match.groups()
        documents[int(key)] = [int(kind), body]
    names = {key: re.search(r'  m_Name: (.*)', body)[1]
             for key, (kind, body) in documents.items() if kind == 1}
    transforms = {int(re.search(r'm_GameObject: \{fileID: (\d+)', body)[1]): key
                  for key, (kind, body) in documents.items() if kind == 4}
    target_names = {"Body": "Innerscene CC0 Urban Scooter", "Steering": "Steering",
                    "FrontWheel": "Front wheel", "RearWheel": "Rear wheel"}
    targets = {}
    for group, name in target_names.items():
        matches = [transforms[g] for g, n in names.items() if n == name]
        assert len(matches) == 1, (group, matches)
        targets[group] = matches[0]
    parts = json.loads((OUTPUT / "part-groups.json").read_text())

    def parent(key):
        return int(re.search(r'm_Father: \{fileID: (\d+)', documents[key][1])[1])

    def vector(key, field, count=3):
        text = re.search(field + r': \{([^}]+)\}', documents[key][1])[1]
        values = tuple(float(v) for v in re.findall(r': ([^,}]+)', text))
        assert len(values) == count
        return values

    def world(key):
        if not key:
            return (0, 0, 0)
        rotation = vector(key, 'm_LocalRotation', 4)
        scale = vector(key, 'm_LocalScale')
        assert all(abs(a-b) < 1e-7 for a, b in zip(rotation, (0, 0, 0, 1))), 'Rotated ancestor; use runtime repair'
        assert all(abs(a-1) < 1e-7 for a in scale), 'Scaled ancestor; use runtime repair'
        return tuple(a+b for a, b in zip(vector(key, 'm_LocalPosition'), world(parent(key))))

    changes = []
    matched = []
    before = {}
    for gameobject, name in names.items():
        if name not in parts:
            continue
        key = transforms[gameobject]
        matched.append(name)
        before[key] = world(key)
        target = targets[parts[name]]
        if parent(key) != target:
            target_world = world(target)
            local = tuple(a-b for a, b in zip(before[key], target_world))
            changes.append((name, key, parent(key), target, local))
    assert set(matched) == set(parts), "Saved scene must contain every source part"

    if changes and not repair:
        raise AssertionError(f"{len(changes)} incorrectly parented parts; run --repair")
    if repair:
        for name, key, previous, target, local in changes:
            for old_parent, add in [(previous, False), (target, True)]:
                body = documents[old_parent][1]
                children = re.search(r'  m_Children:.*?(?=  m_Father:)', body, re.S)[0]
                ids = [int(v) for v in re.findall(r'fileID: (\d+)', children)]
                if add:
                    assert key not in ids
                    ids.append(key)
                else:
                    assert key in ids
                    ids.remove(key)
                replacement = '  m_Children:\n' + ''.join(f'  - {{fileID: {i}}}\n' for i in ids) if ids else '  m_Children: []\n'
                documents[old_parent][1] = body.replace(children, replacement)
            body = documents[key][1]
            body = re.sub(r'  m_Father: \{fileID: \d+\}', f'  m_Father: {{fileID: {target}}}', body)
            body = re.sub(r'  m_LocalPosition: \{[^}]+\}',
                          '  m_LocalPosition: {x: %.9g, y: %.9g, z: %.9g}' % local, body)
            documents[key][1] = body
        for key, expected in before.items():
            assert all(abs(a-b) < 1e-5 for a, b in zip(world(key), expected)), 'World position changed'
        result = header + ''.join(f'--- !u!{kind} &{key}\n{body}' for key, (kind, body) in documents.items())
        if changes:
            backup = OUTPUT / 'FirstRideTest.before-wheel-fix.unity'
            if not backup.exists():
                backup.write_text(original, encoding="utf-8")
            SCENE.write_text(result, encoding="utf-8")
    for name in matched:
        g = next(g for g, n in names.items() if n == name)
        assert parent(transforms[g]) == targets[parts[name]], name
    print(f"PASS {len(matched)} saved scene part parents; {len(changes)} repaired; world positions preserved")
    return [name for name, *_ in changes]


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--repair', action='store_true')
    changes = check_scene(parser.parse_args().repair)
    if changes:
        print('\n'.join(changes))
