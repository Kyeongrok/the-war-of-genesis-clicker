"""대시보드가 번호 대신 이름을 보이도록 어빌리티·인물·전투 이름 표(src/names.js)를 뽑는다.

어빌리티·인물은 게임 자료(assets)에서, 전투의 챕터·이름은 분석 노트 「분석-전투목록」의 전체 표에서 읽는다
(표의 줄 차례가 곧 이야기 순서다). 노트가 다른 데 있으면 그 경로를 인자로 준다 — 없으면 전투 표는 비운다.

쓰기: 저장소 뿌리에서  python tools/stats-worker/make_names.py [분석-전투목록.md 경로]
자료가 바뀌면(어빌리티 이름·새 인물) 다시 돌리고 Worker 를 다시 올린다.
"""
import glob
import io
import json
import os
import sys

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

# 전투 → [챕터, 이름]. 표 줄: | 0010 코어헌터 | 0045 | 코어헌터 훈련장(1) | … — 「(못 찾음)」 줄은 챕터 없이 둔다.
default_notes = os.path.join(os.path.expanduser('~'), 'Documents', 'mv', 'Project', 'the-war-of-genesis', '분석', '분석-전투목록.md')
battle_notes = sys.argv[1] if len(sys.argv) > 1 else default_notes
battles, chapters = {}, []
if os.path.exists(battle_notes):
    for line in io.open(battle_notes, encoding='utf-8'):
        cells = [c.strip() for c in line.split('|')]
        if len(cells) < 5 or not cells[2].isdigit():
            continue
        chapter = cells[1] if cells[1][:4].isdigit() else ''
        name = '' if '못 읽음' in cells[3] else cells[3]
        battles.setdefault(int(cells[2]), [chapter, name])
        if chapter and chapter not in chapters:
            chapters.append(chapter)
else:
    print(f'전투 목록 노트가 없습니다({battle_notes}) - 전투 표는 비웁니다')

out = os.path.join(root, 'tools', 'stats-worker', 'src', 'names.js')
with io.open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write('// make_names.py 가 만든다 — 손으로 고치지 않는다.\n')
    f.write('export const ABILITIES = ' + json.dumps(dict(sorted(abilities.items())), ensure_ascii=False) + ';\n')
    f.write('export const CHARACTERS = ' + json.dumps(dict(sorted(characters.items())), ensure_ascii=False) + ';\n')
    f.write('// 전투 번호 → [챕터, 이름] · CHAPTERS 는 이야기 순서\n')
    f.write('export const BATTLES = ' + json.dumps(dict(sorted(battles.items())), ensure_ascii=False) + ';\n')
    f.write('export const CHAPTERS = ' + json.dumps(chapters, ensure_ascii=False) + ';\n')
print(f'{out}: 어빌리티 {len(abilities)} · 인물 {len(characters)} · 전투 {len(battles)} · 챕터 {len(chapters)}')
