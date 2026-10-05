"""대시보드가 번호 대신 이름을 보이도록 어빌리티·인물 이름 표(src/names.js)를 게임 자료에서 뽑는다.

쓰기: 저장소 뿌리에서  python tools/stats-worker/make_names.py
자료가 바뀌면(어빌리티 이름·새 인물) 다시 돌리고 Worker 를 다시 올린다.
"""
import glob
import io
import json
import os

root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

abilities = {}
for path in glob.glob(os.path.join(root, 'assets', 'data', 'skills', '*.json')):
    skill = json.load(io.open(path, encoding='utf-8'))
    if skill.get('ability') and skill.get('name'):
        abilities[skill['ability']] = skill['name']

characters = {}
for path in glob.glob(os.path.join(root, 'assets', 'characters', '*', 'character.json')):
    who = json.load(io.open(path, encoding='utf-8-sig'))
    characters[who['ChrCode']] = who['Name']

out = os.path.join(root, 'tools', 'stats-worker', 'src', 'names.js')
with io.open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write('// make_names.py 가 만든다 — 손으로 고치지 않는다.\n')
    f.write('export const ABILITIES = ' + json.dumps(dict(sorted(abilities.items())), ensure_ascii=False) + ';\n')
    f.write('export const CHARACTERS = ' + json.dumps(dict(sorted(characters.items())), ensure_ascii=False) + ';\n')
print(f'{out}: 어빌리티 {len(abilities)} · 인물 {len(characters)}')
