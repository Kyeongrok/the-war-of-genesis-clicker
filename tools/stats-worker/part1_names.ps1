# 파트1 이름 표(part1_names.json)를 뽑는다 — make_names.py 가 names.js 에 합친다(사용자 요청 menu-110).
# 파트1 자료는 사용자의 파트1 원본 폴더에서만 읽을 수 있어 make_names.py(파이썬)가 못 읽는다 — 빌드한 WarOfGenesis.Assets.dll 로 읽는다.
# 게임은 파트1 인물 · 어빌리티 · 전투를 20000 + 파트1 번호로 보낸다(Part1Game.ChrBase · BattleBase, Part1SkillBook).
#
# 쓰기: pwsh tools/stats-worker/part1_names.ps1 -Part1 '<파트1 폴더>' [-Dll <WarOfGenesis.Assets.dll>]
param([Parameter(Mandatory)][string]$Part1, [string]$Dll = "$PSScriptRoot\..\..\duel-dx\bin\Debug\net8.0-windows\WarOfGenesis.Assets.dll")
$env:WAROFGENESIS_PART1 = $Part1
Add-Type -Path (Resolve-Path $Dll)
$base = 20000

$characters = [ordered]@{}
foreach ($code in 0..999) {
  $c = [WarOfGenesis.Assets.Part1Chr]::Load($code)
  if ($c -and $c.Name.Trim()) { $characters["$($base + $code)"] = $c.Name.Trim() }
}

$abilities = [ordered]@{}
foreach ($a in [WarOfGenesis.Assets.Part1Tables]::Abilities) {
  if ($a.Name.Trim()) { $abilities["$($base + $a.Id)"] = $a.Name.Trim() }
}

# 전투 → [챕터, 이름] · 분기. 챕터에서 사슬을 따라간다 — 챕터 지도의 전투 장소 · 챕터 502 [전투] · 503 [필드],
# 필드 502 [전투] · 501 [다음 필드], 전투 101 [이어지는 전투] · 104 [끝내고 그 필드로](원본 번호 — Part1Script 가 옮기기 전).
# 처음 닿은 챕터의 것으로 친다. 연대표 칸이 없는 챕터(앞 챕터가 이어 여는 것)는 앞 번호 챕터의 분기를 잇는다.
$battles = [ordered]@{}
$chapters = @()
$branchNames = @('시반 슈미터', '크림슨 크루세이더', '아포칼립스')
$lineOf = @{}
foreach ($e in [WarOfGenesis.Assets.Part1Episode]::LoadAll()) { if (-not $lineOf.ContainsKey($e.ChapterId) -and $e.Line -ge 0 -and $e.Line -lt 3) { $lineOf[$e.ChapterId] = $e.Line } }
$branchOf = @{}
$seenField = @{}
$last = $null
function Targets($events, [int]$code) { @($events | % { $_.Actions } | ? { $_.Code -eq $code -and $_.Args.Length -gt 0 } | % { [int]$_.Args[0] } | ? { $_ -ge 0 }) }
foreach ($id in 0..199) {
  $ch = [WarOfGenesis.Assets.Part1Chapter]::Load($id)
  if (-not $ch -or $ch.Title -match '테스트') { continue }   # 원본의 시험 챕터(84 「전투테스트 - 2번 CD」 따위)는 전투를 가져가지 않게 뺀다
  $label = "파트1 · $('{0:D2}' -f $id) $($ch.Title.Trim())".Trim()
  if ($lineOf.ContainsKey($id)) { $last = "파트1 · $($branchNames[$lineOf[$id]])" }
  $branch = if ($last) { $last } else { '파트1 · 분기 모름' }
  $queue = [System.Collections.Generic.Queue[object]]::new()
  foreach ($b in @($ch.Battles) + (Targets $ch.Events 502)) { if ($b -ge 0) { $queue.Enqueue(@('b', $b)) } }
  foreach ($f in Targets $ch.Events 503) { $queue.Enqueue(@('f', $f)) }
  $any = $false
  while ($queue.Count -gt 0) {
    $kind, $n = $queue.Dequeue()
    if ($kind -eq 'f') {
      if ($seenField.ContainsKey($n)) { continue }
      $seenField[$n] = $true
      $fld = [WarOfGenesis.Assets.Part1Field]::Load($n)
      if (-not $fld) { continue }
      foreach ($b in Targets $fld.Events 502) { $queue.Enqueue(@('b', $b)) }
      foreach ($f in Targets $fld.Events 501) { $queue.Enqueue(@('f', $f)) }
      continue
    }
    $key = "$($base + $n)"
    if ($battles.Contains($key)) { continue }
    $battles[$key] = @($label, ""); $branchOf[$key] = $branch; $any = $true
    $btl = [WarOfGenesis.Assets.Part1Battle]::Load($n)
    if (-not $btl) { continue }
    foreach ($b in Targets $btl.Events 101) { $queue.Enqueue(@('b', $b)) }
    foreach ($f in Targets $btl.Events 104) { $queue.Enqueue(@('f', $f)) }
  }
  if ($any -and $chapters -notcontains $label) { $chapters += $label }
}
foreach ($b in 0..999) {
  $btl = [WarOfGenesis.Assets.Part1Battle]::Load($b)
  if (-not $btl) { continue }
  $name = $btl.Line($btl.TitleLine).Trim()
  $key = "$($base + $b)"
  if ($battles.Contains($key)) { $battles[$key][1] = $name } else { $battles[$key] = @("파트1 · 챕터 모름", $name) }
}
$chapters += "파트1 · 챕터 모름"

$out = Join-Path $PSScriptRoot 'part1_names.json'
[ordered]@{ characters = $characters; abilities = $abilities; battles = $battles; chapters = $chapters; branches = $branchOf; branchOrder = @($branchNames | % { "파트1 · $_" }) + '파트1 · 분기 모름' } |
  ConvertTo-Json -Depth 4 -Compress | Set-Content $out -Encoding utf8NoBOM
"$out : 인물 $($characters.Count) · 어빌리티 $($abilities.Count) · 전투 $($battles.Count) · 챕터 $($chapters.Count)"
