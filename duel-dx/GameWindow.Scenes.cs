using System.Threading;

namespace DuelDx;

/// <summary>창이 여는 장면들 — 필드(<see cref="FieldScene"/>)와 모세스(<see cref="MosesScene"/>)는 따로 선 클래스이고, 처음 쓸 때 만든다.</summary>
internal sealed unsafe partial class GameWindow
{
    /// <summary>FieldScene — 처음 쓸 때 만든다(장면마다 따로 선 클래스).</summary>
    internal FieldScene? _fldScene;
    internal FieldScene Fld => _fldScene ?? LazyInitializer.EnsureInitialized(ref _fldScene, () => new FieldScene(this));

    /// <summary>필드가 열려 있나 — 장면 개체를 만들지 않고 본다(틀마다 여러 번 불린다).</summary>
    internal bool FieldOpen => _fldScene is { FieldOpen: true };

    /// <summary>
    /// 전투 판이 다른 화면(모세스 · 필드 · 타이틀 · 기록 · 연대표 · 챕터 고르기)에 덮여 있나 — 새 UI 층에 그리는 전투 창(HUD · 정보 창 · 목록)은 이때 안 그린다.
    /// 판에 그릴 때는 뒤에 그리는 그 화면이 덮어 줬지만, 층은 늘 판 위라 비쳐 나온다(menu-57).
    /// </summary>
    internal bool BattleCovered => Mos._mosesOpen || FieldOpen || TitleScr._titleOpen || RecordsScr._recordsOpen || EpisodesScr._episodesOpen || ChaptersScr._chaptersOpen;

    /// <summary>MosesScene — 처음 쓸 때 만든다(장면마다 따로 선 클래스).</summary>
    internal MosesScene? _mosScene;
    internal MosesScene Mos => _mosScene ?? LazyInitializer.EnsureInitialized(ref _mosScene, () => new MosesScene(this));

    internal AcrostSkill? _acrostAb;
    internal AcrostSkill AcrostAb => _acrostAb ?? LazyInitializer.EnsureInitialized(ref _acrostAb, () => new AcrostSkill(this));

    internal AstralArrowSkill? _astralArrowAb;
    internal AstralArrowSkill AstralArrowAb => _astralArrowAb ?? LazyInitializer.EnsureInitialized(ref _astralArrowAb, () => new AstralArrowSkill(this));

    internal BladeMissileSkill? _bladeMissileAb;
    internal BladeMissileSkill BladeMissileAb => _bladeMissileAb ?? LazyInitializer.EnsureInitialized(ref _bladeMissileAb, () => new BladeMissileSkill(this));
    internal ForcetrulSkill? _forcetrulAb;
    internal ForcetrulSkill ForcetrulAb => _forcetrulAb ?? LazyInitializer.EnsureInitialized(ref _forcetrulAb, () => new ForcetrulSkill(this));
    internal CycloneSkill? _cycloneAb;
    internal CycloneSkill CycloneAb => _cycloneAb ?? LazyInitializer.EnsureInitialized(ref _cycloneAb, () => new CycloneSkill(this));
    internal OzmaSkill? _ozmaAb;
    internal OzmaSkill OzmaAb => _ozmaAb ?? LazyInitializer.EnsureInitialized(ref _ozmaAb, () => new OzmaSkill(this));
    internal ElementalSkill? _elementalAb;
    internal ElementalSkill ElementalAb => _elementalAb ?? LazyInitializer.EnsureInitialized(ref _elementalAb, () => new ElementalSkill(this));
    internal TachyrionSkill? _tachyrionAb;
    internal TachyrionSkill TachyrionAb => _tachyrionAb ?? LazyInitializer.EnsureInitialized(ref _tachyrionAb, () => new TachyrionSkill(this));
    internal FxTimeline? _fxLine;
    internal FxTimeline FxLine => _fxLine ?? LazyInitializer.EnsureInitialized(ref _fxLine, () => new FxTimeline(this));
    internal IdeaCannonSkill? _ideaCannonAb;
    internal IdeaCannonSkill IdeaCannonAb => _ideaCannonAb ?? LazyInitializer.EnsureInitialized(ref _ideaCannonAb, () => new IdeaCannonSkill(this));
    internal GravityFieldSkill? _gravityFieldAb;
    internal GravityFieldSkill GravityFieldAb => _gravityFieldAb ?? LazyInitializer.EnsureInitialized(ref _gravityFieldAb, () => new GravityFieldSkill(this));

    internal MeteorSkill? _meteorAb;
    internal MeteorSkill MeteorAb => _meteorAb ?? LazyInitializer.EnsureInitialized(ref _meteorAb, () => new MeteorSkill(this));

    internal HeavenEarthSkill? _heavenEarthAb;
    internal HeavenEarthSkill HeavenEarthAb => _heavenEarthAb ?? LazyInitializer.EnsureInitialized(ref _heavenEarthAb, () => new HeavenEarthSkill(this));

    internal Part1SwordSkill? _part1SwordAb;
    internal Part1SwordSkill Part1SwordAb => _part1SwordAb ?? LazyInitializer.EnsureInitialized(ref _part1SwordAb, () => new Part1SwordSkill(this));

    internal NineCrusaderSkill? _nineCrusaderAb;
    internal NineCrusaderSkill NineCrusaderAb => _nineCrusaderAb ?? LazyInitializer.EnsureInitialized(ref _nineCrusaderAb, () => new NineCrusaderSkill(this));

    internal TeleportSkill? _teleportAb;
    internal TeleportSkill TeleportAb => _teleportAb ?? LazyInitializer.EnsureInitialized(ref _teleportAb, () => new TeleportSkill(this));

    internal FinisherPreludeSkill? _finisherPreludeAb;
    internal FinisherPreludeSkill FinisherPreludeAb => _finisherPreludeAb ?? LazyInitializer.EnsureInitialized(ref _finisherPreludeAb, () => new FinisherPreludeSkill(this));

    internal KnockbackSkill? _knockbackAb;
    internal KnockbackSkill KnockbackAb => _knockbackAb ?? LazyInitializer.EnsureInitialized(ref _knockbackAb, () => new KnockbackSkill(this));

    internal PushSkill? _pushAb;
    internal PushSkill PushAb => _pushAb ?? LazyInitializer.EnsureInitialized(ref _pushAb, () => new PushSkill(this));

    internal SpecialHitsSkill? _specialHitsAb;
    internal SpecialHitsSkill SpecialHitsAb => _specialHitsAb ?? LazyInitializer.EnsureInitialized(ref _specialHitsAb, () => new SpecialHitsSkill(this));

    internal StagingSkill? _stagingAb;
    internal StagingSkill StagingAb => _stagingAb ?? LazyInitializer.EnsureInitialized(ref _stagingAb, () => new StagingSkill(this));

    internal LegionStageSkill? _legionStageAb;
    internal LegionStageSkill LegionStageAb => _legionStageAb ?? LazyInitializer.EnsureInitialized(ref _legionStageAb, () => new LegionStageSkill(this));

    internal UnitFxSkill? _unitFxAb;
    internal UnitFxSkill UnitFxAb => _unitFxAb ?? LazyInitializer.EnsureInitialized(ref _unitFxAb, () => new UnitFxSkill(this));

    internal TitleScreen? _titleScr;
    internal TitleScreen TitleScr => _titleScr ?? LazyInitializer.EnsureInitialized(ref _titleScr, () => new TitleScreen(this));

    internal EpisodesScreen? _episodesScr;
    internal EpisodesScreen EpisodesScr => _episodesScr ?? LazyInitializer.EnsureInitialized(ref _episodesScr, () => new EpisodesScreen(this));

    internal ChaptersScreen? _chaptersScr;
    internal ChaptersScreen ChaptersScr => _chaptersScr ?? LazyInitializer.EnsureInitialized(ref _chaptersScr, () => new ChaptersScreen(this));

    internal RecordsScreen? _recordsScr;
    internal RecordsScreen RecordsScr => _recordsScr ?? LazyInitializer.EnsureInitialized(ref _recordsScr, () => new RecordsScreen(this));

    internal SlotsScreen? _slotsScr;
    internal SlotsScreen SlotsScr => _slotsScr ?? LazyInitializer.EnsureInitialized(ref _slotsScr, () => new SlotsScreen(this));

    internal ProgressScreen? _progressScr;
    internal ProgressScreen ProgressScr => _progressScr ?? LazyInitializer.EnsureInitialized(ref _progressScr, () => new ProgressScreen(this));

    internal TuningScreen? _tuningScr;
    internal TuningScreen TuningScr => _tuningScr ?? LazyInitializer.EnsureInitialized(ref _tuningScr, () => new TuningScreen(this));

    internal BattleScene? _btl;
    internal BattleScene Btl => _btl ?? LazyInitializer.EnsureInitialized(ref _btl, () => new BattleScene(this));

    internal StatusScreen? _statusScr;
    internal StatusScreen StatusScr => _statusScr ?? LazyInitializer.EnsureInitialized(ref _statusScr, () => new StatusScreen(this));

    internal TalkBox? _tlk;
    internal TalkBox Tlk => _tlk ?? LazyInitializer.EnsureInitialized(ref _tlk, () => new TalkBox(this));

    internal CutscenePlayer? _cut;
    internal CutscenePlayer Cut => _cut ?? LazyInitializer.EnsureInitialized(ref _cut, () => new CutscenePlayer(this));
    internal MoviePlayer? _mov;
    internal MoviePlayer Mov => _mov ?? LazyInitializer.EnsureInitialized(ref _mov, () => new MoviePlayer(this));

    internal SystemMenu? _sys;
    internal SystemMenu Sys => _sys ?? LazyInitializer.EnsureInitialized(ref _sys, () => new SystemMenu(this));

    internal PartyStore? _partySt;
    internal PartyStore PartySt => _partySt ?? LazyInitializer.EnsureInitialized(ref _partySt, () => new PartyStore(this));

    internal FlagStore? _flagSt;
    internal FlagStore FlagSt => _flagSt ?? LazyInitializer.EnsureInitialized(ref _flagSt, () => new FlagStore(this));

    internal DevScreen? _devScr;
    internal DevScreen DevScr => _devScr ?? LazyInitializer.EnsureInitialized(ref _devScr, () => new DevScreen(this));
    internal CharEditScreen? _charEditScr;
    internal CharEditScreen CharEditScr => _charEditScr ?? LazyInitializer.EnsureInitialized(ref _charEditScr, () => new CharEditScreen(this));
    private HiResBackdrop? _hiBack;
    internal HiResBackdrop HiBack => _hiBack ?? LazyInitializer.EnsureInitialized(ref _hiBack, () => new HiResBackdrop(this));
    internal BattleStats? _stats;
    internal BattleStats Stats => _stats ?? LazyInitializer.EnsureInitialized(ref _stats, () => new BattleStats(this));
    internal StatsScreen? _statsScr;
    internal StatsScreen StatsScr => _statsScr ?? LazyInitializer.EnsureInitialized(ref _statsScr, () => new StatsScreen(this));
    internal SkillBookScreen? _skillBookScr;
    internal SkillBookScreen SkillBookScr => _skillBookScr ?? LazyInitializer.EnsureInitialized(ref _skillBookScr, () => new SkillBookScreen(this));
    internal StatsConsentScreen? _consentScr;
    internal StatsConsentScreen ConsentScr => _consentScr ?? LazyInitializer.EnsureInitialized(ref _consentScr, () => new StatsConsentScreen(this));
    internal SettingsScreen? _settingsScr;
    internal SettingsScreen SettingsScr => _settingsScr ?? LazyInitializer.EnsureInitialized(ref _settingsScr, () => new SettingsScreen(this));
    internal TimedHit? _timedHitFx;
    internal TimedHit TimedHitFx => _timedHitFx ?? LazyInitializer.EnsureInitialized(ref _timedHitFx, () => new TimedHit(this));
    internal TimedHitEditScreen? _timedEditScr;
    private HiCanvas? _hi;
    /// <summary>창 해상도로 그리는 덧그림 층 — 새 UI 가 여기에 그린다.</summary>
    internal HiCanvas Hi => _hi ?? LazyInitializer.EnsureInitialized(ref _hi, () => new HiCanvas(this));

    internal TimedHitEditScreen TimedEditScr => _timedEditScr ?? LazyInitializer.EnsureInitialized(ref _timedEditScr, () => new TimedHitEditScreen(this));
    internal GamePad? _pad;
    internal GamePad Pad => _pad ?? LazyInitializer.EnsureInitialized(ref _pad, () => new GamePad(this));
    internal ReleaseNotesScreen? _notesScr;
    internal ReleaseNotesScreen NotesScr => _notesScr ?? LazyInitializer.EnsureInitialized(ref _notesScr, () => new ReleaseNotesScreen(this));
}
