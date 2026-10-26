using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 시험 전용: <c>DUELDX_P1AUTO=1</c> 이면 파트1 이야기를 처음부터 저절로 따라간다 — 화면 밖에서 챕터 · 필드 · 전투 흐름이 막히거나 죽는 곳을 찾으려고 둔 것이다.
/// </summary>
/// <remarks>
/// <para>파트2 타이틀 → PART 1 → NEW GAME. 대사는 넘기고, 필드는 다 돌면 나가고, 레벨업은 첫 후보를 고르고, 마을 · 일행 창은 바로 닫는다.
/// 챕터 지도에서는 안 깬 전투 장소를 차례로 한 번씩 누르고, 더 누를 것이 없으면 턴을 넘긴다(턴 한도 한 턴 앞까지).
/// 그래도 챕터가 안 끝나면 「막힘」을 적고 연대표의 다음 챕터로 간다. 챕터가 끝나면(501 → 연대표) 연대표 차례로 안 끝낸 첫 열린 챕터를 연다.
/// 지면(파트1 타이틀로 돌아오면) CONTINUE 로 잇는다.</para>
/// <para>전투는 DUELDX_AUTOPLAY · DUELDX_TESTSKIP 이 돌린다. 한 일은 <c>testrun p1auto …</c> 줄로 남는다(DUELDX_TESTRUN 이 켜져 있어야 적힌다).</para>
/// </remarks>
internal sealed class Part1Auto(GameWindow host)
{
    internal static readonly bool On = Environment.GetEnvironmentVariable("DUELDX_P1AUTO") is "1" or "tutorial" or "continue";

    /// <summary><c>DUELDX_P1AUTO=continue</c> — NEW GAME 대신 CONTINUE 로 세이브(DUELDX_SAVEDIR 의 part1.json)에서 잇는다.</summary>
    private static readonly bool FromSave = Environment.GetEnvironmentVariable("DUELDX_P1AUTO") == "continue";

    /// <summary><c>DUELDX_P1AUTO=tutorial</c> — NEW GAME 대신 TUTORIAL 을 누르고, 연대표에 닿으면 멈춘다.</summary>
    private static readonly bool Tutorial = Environment.GetEnvironmentVariable("DUELDX_P1AUTO") == "tutorial";

    private double _nextAt;
    private bool _newGameDone;
    private int _chapterId = -1, _turns, _losses;
    private readonly HashSet<int> _tried = [];
    private readonly HashSet<int> _stuck = [];

    private static void Log(string line) => GameWindow.TestRunTrace("p1auto " + line);

    internal void Tick()
    {
        if (!On || host._lastTime < _nextAt || host._fadeOutStart >= 0) return;
        _nextAt = host._lastTime + 0.4;
        var shell = host.Part1TitleScr;
        if (!shell._open)
        {
            // 전투 중이면 자동 진행에 맡긴다. 파트2 타이틀이면 PART 1 로 들어간다.
            if (host._battleLoaded && host.Btl._outcome.Length == 0) return;
            if (host.TitleScr._titleOpen && !_newGameDone) { Log("enter part1"); shell.PressEntry(); Wait(); }
            return;
        }
        if (shell.Party._open) { shell.Party.OnKey(Win32.VK_ESCAPE); Wait(); return; }
        if (shell.Town._open) { shell.Town.OnKey(Win32.VK_ESCAPE); Wait(); return; }
        if (shell.Field._open) { shell.Field.OnClick(); return; }
        if (shell.Chapter._open) { ChapterStep(shell.Chapter); return; }
        if (shell.Timeline._open && Tutorial) { Log("tutorial done — timeline"); Wait(9999); return; }
        if (shell.Timeline._open) { NextChapter(shell, "timeline"); return; }
        // 파트1 타이틀 — 처음이면 NEW GAME, 진 뒤면 CONTINUE.
        if (!_newGameDone) { _newGameDone = true; Log(Tutorial ? "tutorial" : FromSave ? "continue from save" : "new game"); shell.Press(Tutorial ? Part1TitleScreen.ButtonTutorial : FromSave ? Part1TitleScreen.ButtonContinue : Part1TitleScreen.ButtonNewGame); }
        else { _losses++; Log($"continue after loss #{_losses}"); shell.Press(Part1TitleScreen.ButtonContinue); }
        Wait();
    }

    private void Wait(double seconds = 1.5) => _nextAt = host._lastTime + seconds;

    private void ChapterStep(Part1ChapterScreen screen)
    {
        if (screen._chapter is not { } chapter) return;
        if (screen.Events.Talk != null || screen.Picking) { screen.OnKey(Win32.VK_RETURN); return; }
        if (screen.WindowOpen || screen._mode != Part1ChapterScreen.Mode.Map) { screen.OnKey(Win32.VK_ESCAPE); return; }
        if (chapter.Id != _chapterId)
        {
            _chapterId = chapter.Id;
            _turns = 0;
            _tried.Clear();
            Log($"chapter {chapter.Id} places {chapter.Places.Count} battles [{string.Join(",", chapter.Battles)}] group {host.Part1.CurrentGroup} [{string.Join(",", host.Part1.GroupMembers())}]");
        }
        // 안 깬 전투 장소 · 안 가 본 마을을 차례로 한 번씩.
        for (int i = 0; i < chapter.Places.Count; i++)
        {
            var place = chapter.Places[i];
            if (_tried.Contains(i) || !(place.IsBattle && !host.Part1.Cleared.Contains(place.Target) || place.IsTown)) continue;
            _tried.Add(i);
            Log($"chapter {chapter.Id} press place {i} ({(place.IsBattle ? $"battle {place.Target}" : $"town {place.Target}")})");
            screen.GoTo(i);   // 이웃 · 턴 제한 없이 곧장 옮겨 들어간다
            Wait();
            return;
        }
        int limit = chapter.Header.Length > 10 && chapter.Header[10] > 0 ? chapter.Header[10] : 30;
        if (host.Part1.MapTurns.GetValueOrDefault(chapter.Id) + 1 < limit && _turns < 20)
        {
            _turns++;
            _tried.RemoveWhere(i => chapter.Places[i].IsBattle && !host.Part1.Cleared.Contains(chapter.Places[i].Target) && _turns % 3 == 0);   // 가끔 다시 눌러 본다
            Log($"chapter {chapter.Id} end turn {host.Part1.MapTurns.GetValueOrDefault(chapter.Id) + 1}");
            screen.Press(Part1ChapterScreen.ButtonHit + Part1ChapterScreen.ButtonEndTurn);
            Wait(1);
            return;
        }
        Log($"chapter {chapter.Id} STUCK (turns {_turns}, cleared [{string.Join(",", chapter.Battles.Where(host.Part1.Cleared.Contains))}])");
        _stuck.Add(chapter.Id);
        host.Part1.FinishedChapters.Add(chapter.Id);
        NextChapter(host.Part1TitleScr, "stuck");
    }

    /// <summary>연대표 차례로 안 끝낸 첫 챕터를 연다(막힌 챕터는 건너뛴다).</summary>
    private void NextChapter(Part1TitleScreen shell, string why)
    {
        var next = Part1Episode.LoadAll().Select(e => e.ChapterId).FirstOrDefault(c => !host.Part1.FinishedChapters.Contains(c) && !_stuck.Contains(c), -1);
        if (next < 0) { Log($"done ({why}) — finished {host.Part1.FinishedChapters.Count}, stuck [{string.Join(",", _stuck)}]"); Wait(9999); return; }
        Log($"open chapter {next} ({why})");
        shell.Timeline._open = false;
        host.LeaveScene(() => shell.Chapter.Open(next));
        Wait();
    }
}
