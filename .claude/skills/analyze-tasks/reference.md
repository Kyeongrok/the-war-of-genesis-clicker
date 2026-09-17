# 알려진 사실 (분석 시작점)

자세한 근거는 옵시디안 `분석\` 노트에 있다. 여기는 다시 파지 않도록 요점만 모은다. 새로 확정한 것은 이 파일에도 한 줄 더한다.

## 파일 묶음
- 게임 자료 폴더마다 `<폴더>.idx` + `<폴더>.pak`(또는 `Obs00~03.pak`) 과 낱장 파일. 낱장이 우선.
  `.idx` 레코드 36바이트, 파일명·pak 이름은 XOR 0xFF. → `tools/re/pak_extract.py`
- `Bgr/*.bgr` = JPEG 일러스트(640×480). 전투 배경 아님.

## 텍스트 (TXR/Txr.dat)
- 번호 하나로 찾는 전역 표. DLL 로더 `LoadTextData 0x1004a190`: 머리 10바이트 뒤 `(u16 번호, u32 위치, u32 길이)` 레코드. 조회 함수 `0x1004a3f0`(인자 = 번호).
- **주의 (2026-09-18 고침)**: 예전 `parse_txr` 는 번호를 한 칸 밀려 읽었다. 2026-09-17 까지의 노트에 적힌 이름·번호는 한 칸 밀렸을 수 있다(노트 머리 정정 참고). 지금 `tools/extract_character.py parse_txr`·`TxrTable.cs` 는 DLL 과 같다.
- 쓸모 있는 번호대(고친 값): 0~26 메뉴(1 Pannel, 2 Attack, 3 Ability, 4 Item, 5 Rest, 6 Status, 7 Job, 8 System, 9 Info, 10 전직표, 11 능력치, 12 획득한 어빌리티, 13 획득할 수 있는 어빌리티, 14 장비·아이템, 26 Turn),
  34~41 능력치(LP·PSY·DEX·DEP·TP·CTP·STP·SOUL), 156~161 ATK·ACR·RDP·HP·LEVEL·EXP, 162 무속성, 163 상태이상, 166 장착 어빌리티,
  141~155 체질 이름(151 에텔체 … 155 메텔체), 878~882 형(일반·공격·방어·고속·보조), 894~898 계열(사이클론·타키리온·포스트럴·아크로스트·오즈마),
  1375~1380 링 메뉴 설명, 2284~2313 챕터 제목(2284 코어헌터), 이름 예: 463 살라딘, 474 제이슨, 475 죠안.

## 전투
- 전투 상태 `[CBattle+0x4cd0]`, 디스패처 `jmp [eax*4+0x100663f0]`(eax = 상태−1).
  9 메뉴 `0x10068ff0`, 10 일반공격 대상 `0x10069330`, 11 어빌리티 대상 `0x10069a90`, 12 이동 `0x10069c50`.
- 링 메뉴: 생성 `0x1006b810` → 창 생성자 `0x100e0a40`(리소스 0x46d, 반지름 89, 유닛 머리 위 40px),
  키 처리 `0x100e0ec0`(W = 일반공격 0x40a), 결과 나누기 `0x10068c60`.
- `Btl/NNNN.btl`: 헤더 뒤 0x18 부터 29바이트 유닛 레코드(순번, Chr 코드, x, y, …). 헤더 둘째 워드 → `Map/NNNN.map` 둘째 워드 → `Obt/NNNN.obt`.
- `Obt/NNNN.obt`: `LoadObtFile` `0x10030840`. 40×8 RGB555 띠 타일 + 배치표. 전투 칸 40×32px. → `WarOfGenesis.Assets/ObtMap.cs`
- 코어헌터 훈련장 첫 전투 = `Btl 0045`(아군 죠안 221·살라딘 219·제이슨 62, 적 가이아리더 193), 맵 `Obt 0153`.
- 전투 로딩·턴(TP 게이지)·TP/CTP/STP·Soul: [[분석-전투]]. 도구 `btl_dump.py`, `turn_order.py`, `tp_calc.py`, `soul_costs.py`, `func_calls.py`(함수 호출 나무).

## 캐릭터
- `Chr/NNNN.chr` 90바이트, `CChr.Load` `0x10031530`. 배치표는 `tools/re/chr_dump.py` 머리말.
  +2 이름, +8 sprite, +10 face, +12 칭호·소속 텍스트, **+14 체질**(0 무속성·1 에텔(사이클론)·2 멘탈(타키리온)·3 아스트럴(포스트럴)·4 코절(아크로스트)·5 메텔(오즈마), 255 NPC — 죠안 화면 "오즈마"로 확인),
  **+15 직업 = Job.dat 번호**, +42 장비 6칸(무기·갑옷·아뮬렛·반지·벨트·신발), +56 (어빌리티, 레벨)×8.
- `Dat/Dep.dat`: `LoadSystemData`, 전역 `0x101b6860`, 16 레코드 = 직업 묶음(사이클론·타키리온·포스트럴·아크로스트·오즈마 × 단계), 각 레코드에 직업 번호들.
- `Dat/Job.dat`: `LoadJobData`, 전역 `0x101b6874`. 파일 레코드 67바이트, 53 오프셋에 체질별 직업 이름 TXR 6칸.
- `Dat/Itm.dat`: `LoadItemData`, 전역 `0x101b687c`. 파일 레코드 48바이트(번호, 이름, 가격 u32, **종류 u8** …). 종류: 0 VES, 1 머리, 2 갑옷, 3 신발, 4 벨트, 5 반지, 6 아뮬렛, 7 캡슐, 8 리본, 9 요요, 10 사진기, 11 크로, 12 쌍권총, 13 시가, 14 일반검, 15 대검, 16 사용, 17 포이즌 포트, 19 적용 무기.
- 패널: Status 쪽 캐릭터 패널 `0x10034050`(칭호·계열·직업·HP·LEVEL…), 전투 중 유닛 패널 `0x100d3d70`(HP·LEVEL·RDP·STP·DEP·SOUL·ATK·ACR). 텍스트를 상수로 부르는 곳 목록은 전체 디스어셈블에서 `call 0x1004a3f0` 직전 push 를 모으면 나온다.
- 다른 dat 로더: `LoadItemData`(Itm.dat), `LoadAbilityData`(0000~0012.att), `LoadValueData`(Num.dat), `LoadStData`(Sta.dat), For/Dmg/Lev.dat.

## 몸짓 (Obs)
- `Obs/NNNN.obs`: 몸짓벌(subentry) 여러 개 × 장(slot). 둘째 벌부터는 앞 벌 끝에서 찾는다. → `WarOfGenesis.Assets/ObsSprite.cs`
- **동작표는 Obs 안에 있다**: 벌 색인표 뒤 `u16 모션 수, u16 최대번호, (u16 번호, u32 위치)×m`. 모션 = 시간줄 키(26바이트: 종류·시작틱·길이·인자10; 종류0 그림(벌,장), 2 다른 Obs 모션). 로더 `0x1002fa00`.
- 모션 번호 = 동작×3 + 방향(0 뒤, 1 옆, 2 앞; 3 은 옆). `SetAction 0x10072820`. 0 서기, 1 걷기. 걷기 컷 손표 `WalkCycles.cs` 는 이걸로 대체 가능.
- 스킬: `Dat/*.att` = work 레코드(62바이트, 로더 문자열은 LoadWorkData), `Abi/*.abi` = 어빌리티(26바이트, 레벨→work). `.chr` +19 = 기본공격 work. 실행 `0x1007ca40`: +0x3f 준비 동작 → work 번호 switch → 핸들러(동작 상수). → `tools/re/skill_motion.py`

## 실행 환경
- 실제 게임: `Desktop\ETC\DGGL\Games\G3P2DVD_Win95_230710` (DOSBox + Win95 이미지). 메모리 읽기 `tools/find_current_battle.py`.
