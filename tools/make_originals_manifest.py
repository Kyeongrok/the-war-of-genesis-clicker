"""배포판이 켤 때 사용자의 원본 게임 폴더에서 꺼내 둘 파일 목록(`assets/originals.txt`)을 만든다 — 게임 쪽은 `WarOfGenesis.Assets/OriginalAssets.cs`.

    python tools/make_originals_manifest.py

원본 게임의 자료(그림·소리·자료 표)는 저장소에도 릴리즈에도 싣지 않는다(.gitignore). 개발 기계의 `assets` 에는 그대로 놓여 있어서,
이 도구가 그 폴더를 훑어 「원본에서 온 파일」의 자리만 적는다 — 내용은 싣지 않는다.

  * 우리 파일(`.json` · 아이콘)과 원본에서 풀어 만든 것(`effects/mov` 의 PNG — 원본 Bink 영상은 게임이 못 읽는다)은 뺀다.
  * `tools/asset_pack_order.txt` 의 큰 파일(음악 · 전투 맵 · 첫 챕터 뒤의 인물 그림 · 모세스 그림 · 큰 효과음)도 뺀다 —
    그것들은 찾는 그때 원본 폴더에서 가져온다(`duel-dx/AssetPack.cs`).

남는 것은 게임이 폴더를 훑어 읽는 것들(자료 표 · 효과 그림 · 효과음 · 첫 화면 그림)이라 켤 때 미리 차려 둔다.
새 원본 파일을 `assets` 에 더했으면 이 도구를 다시 돌려 목록을 커밋한다.
"""
import os

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..'))
OWN = ('.json', '.ico', '.png', '.bak', '.txt')


def main():
    assets = os.path.join(ROOT, 'assets')
    lazy = {l.strip() for l in open(os.path.join(ROOT, 'tools', 'asset_pack_order.txt'), encoding='utf-8') if l.strip()}
    names = []
    for folder, _, files in os.walk(assets):
        for f in files:
            rel = os.path.relpath(os.path.join(folder, f), assets).replace(os.sep, '/')
            if f.lower().endswith(OWN) or rel in lazy:
                continue
            names.append(rel)
    names.sort()
    with open(os.path.join(assets, 'originals.txt'), 'w', encoding='utf-8', newline='\n') as out:
        out.write('# tools/make_originals_manifest.py 가 만든다 — 켤 때 원본 게임 폴더에서 꺼내 둘 파일(자리만, 내용은 싣지 않는다).\n')
        out.write('\n'.join(names) + '\n')
    print(len(names), 'files →', 'assets/originals.txt')


if __name__ == '__main__':
    main()
