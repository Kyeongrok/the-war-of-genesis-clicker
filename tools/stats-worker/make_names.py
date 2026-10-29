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
import struct
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

# 분기 — 전투 → 분기 이름(사용자 요청 menu-110: 대시보드는 분기마다 판 수만 보인다). 파트2 는 연대표(EPS/Episode.dat)의 두 줄 —
# 왼쪽 Episode 4 「영혼의 검」 · 오른쪽 Episode 5 「뫼비우스의 우주」(분석-UI). 한 줄 40바이트, 낱말 7 이 챕터(EpisodesScreen.Episodes).
part2_branches = ['파트2 · Episode 4 영혼의 검', '파트2 · Episode 5 뫼비우스의 우주']
side_of = {}
eps_path = os.path.join(root, 'assets', 'data', 'EPS', 'Episode.dat')
if os.path.exists(eps_path):
    eps = open(eps_path, 'rb').read()
    for r in range(struct.unpack_from('<H', eps, 2)[0]):
        for side in range(2):
            o = 4 + 40 * r + 18 * side + 4 + 2 * 7
            if o + 2 <= len(eps) and struct.unpack_from('<h', eps, o)[0] > 0:
                side_of.setdefault(struct.unpack_from('<h', eps, o)[0], side)
branches = {bid: part2_branches[side_of[int(chapter[:4])]] for bid, (chapter, _) in battles.items()
            if chapter[:4].isdigit() and int(chapter[:4]) in side_of}
branch_order = part2_branches + ['파트2 · 분기 모름']

# 파트1 — 20000 + 파트1 번호(인물 · 어빌리티 · 전투 · 분기). 파트1 원본 폴더에서만 읽혀 part1_names.ps1 이 따로 뽑아 둔 것을 합친다(menu-110).
part1_path = os.path.join(root, 'tools', 'stats-worker', 'part1_names.json')
if os.path.exists(part1_path):
    part1 = json.load(io.open(part1_path, encoding='utf-8-sig'))
    characters.update({int(k): v for k, v in part1['characters'].items()})
    abilities.update({int(k): v for k, v in part1['abilities'].items()})
    battles.update({int(k): v for k, v in part1['battles'].items()})
    chapters += [c for c in part1['chapters'] if c not in chapters]
    branches.update({int(k): v for k, v in part1.get('branches', {}).items()})
    branch_order += part1.get('branchOrder', [])
else:
    print(f'파트1 이름 표가 없습니다({part1_path}) - part1_names.ps1 로 뽑습니다')

out = os.path.join(root, 'tools', 'stats-worker', 'src', 'names.js')
with io.open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write('// make_names.py 가 만든다 — 손으로 고치지 않는다.\n')
    f.write('export const ABILITIES = ' + json.dumps(dict(sorted(abilities.items())), ensure_ascii=False) + ';\n')
    f.write('export const CHARACTERS = ' + json.dumps(dict(sorted(characters.items())), ensure_ascii=False) + ';\n')
    f.write('// 전투 번호 → [챕터, 이름] · CHAPTERS 는 이야기 순서\n')
    f.write('export const BATTLES = ' + json.dumps(dict(sorted(battles.items())), ensure_ascii=False) + ';\n')
    f.write('export const CHAPTERS = ' + json.dumps(chapters, ensure_ascii=False) + ';\n')
    f.write('// 전투 번호 → 분기 · BRANCH_ORDER 는 보이는 차례(분기를 모르는 전투는 그 파트의 「분기 모름」)\n')
    f.write('export const BRANCHES = ' + json.dumps(dict(sorted(branches.items())), ensure_ascii=False) + ';\n')
    f.write('export const BRANCH_ORDER = ' + json.dumps(branch_order, ensure_ascii=False) + ';\n')
print(f'{out}: 어빌리티 {len(abilities)} · 인물 {len(characters)} · 전투 {len(battles)} · 챕터 {len(chapters)} · 분기 붙은 전투 {len(branches)}')
