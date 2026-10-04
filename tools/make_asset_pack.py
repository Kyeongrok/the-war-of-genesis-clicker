"""설치 꾸러미에서 빼고 따로 받는 자산(큰 음악 · 전투 맵)의 꾸러미를 만든다 — 게임 쪽은 `duel-dx/AssetPack.cs`.

    python tools/make_asset_pack.py <내보낼 폴더>

`tools/asset_pack_order.txt`(받는 차례 — 코어헌터 1챕터, 베라모드 1챕터, 그 뒤 챕터 순으로 `bgm/NNNN.bgm` · `maps/NNNN.obt` 한 줄씩)를 읽어

  * <내보낼 폴더>에 파일을 납작한 이름(`bgm_0019.bgm`, `maps_0153.obt`)으로 복사하고 `pack.json`(이름 · 크기 · sha256, 차례대로)을 쓴다
    — 이것들을 GitHub 릴리즈 `assets-pack-1`(prerelease)에 올린다:
        gh release create assets-pack-1 --prerelease --latest=false --title "자산 꾸러미 1 (음악 · 전투 맵)" --notes "게임이 켜질 때 받는 파일" <폴더>/*
  * `duel-dx/AssetPack.props` 를 다시 쓴다 — 두 csproj 가 이 목록의 파일을 설치 꾸러미에서 뺀다.

차례 파일은 챕터 → 전투 → 맵·음악을 따라가 만든 것이다(타이틀 0021 · 연대표 3391 음악과 2MB 보다 작은 음원은 설치판에 남긴다).
인물 그림(`characters/*/*.obs`)과 40KB 이상 효과음(`sounds`)도 같은 기준(첫 화면·첫 챕터 전투들이 쓴 것은 남김)으로 보낸다.
모세스 화면 그림(`moses/obs` · `moses/bgr`)은 100KB 이상이고 `tools/asset_pack_keep.txt`(타이틀·기록·모세스 여덟 페이지·스테이터스·첫 전투·첫 챕터 장면을
화면 밖에서 돌려 `DUELDX_ASSETLOG` 로 적은, 첫 화면들이 쓰는 그림)에 없는 것만 꾸러미로 보낸다 — 첫 챕터 맵·음악 바로 뒤 차례.
"""
import hashlib
import json
import os
import shutil
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..'))


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    names = [l.strip() for l in open(os.path.join(ROOT, 'tools', 'asset_pack_order.txt'), encoding='utf-8') if l.strip()]
    files = []
    seen = set()
    for name in names:
        src = os.path.join(ROOT, 'assets', *name.split('/'))
        # 릴리즈 파일 이름은 ASCII 만 — 인물 폴더의 한글 이름은 뺀다(characters/0221_죠안/0338.obs → characters_0221__0338.obs).
        asset = ''.join(ch for ch in name.replace('/', '_') if ord(ch) < 128)
        assert asset not in seen, asset
        seen.add(asset)
        data = open(src, 'rb').read()
        shutil.copyfile(src, os.path.join(out, asset))
        files.append({'name': name, 'asset': asset, 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
    with open(os.path.join(out, 'pack.json'), 'w', encoding='utf-8') as f:
        json.dump({'files': files}, f, indent=0)
    lines = ['<Project>',
             '  <!-- tools/make_asset_pack.py 가 만든다 — 설치 꾸러미에 싣지 않고 따로 받는 자산(duel-dx/AssetPack.cs). 손으로 고치지 않는다. -->',
             '  <ItemGroup>']
    lines += [f'    <PackedAsset Include="..\\assets\\{n.replace("/", chr(92))}" />' for n in names]
    lines += ['  </ItemGroup>', '</Project>', '']
    with open(os.path.join(ROOT, 'duel-dx', 'AssetPack.props'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines))
    print(len(files), 'files', sum(x['size'] for x in files) >> 20, 'MB →', out)


if __name__ == '__main__':
    main()
