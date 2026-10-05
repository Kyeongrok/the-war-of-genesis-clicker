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

    /// <summary>MosesScene — 처음 쓸 때 만든다(장면마다 따로 선 클래스).</summary>
    internal MosesScene? _mosScene;
    internal MosesScene Mos => _mosScene ?? LazyInitializer.EnsureInitialized(ref _mosScene, () => new MosesScene(this));

    internal AcrostSkill? _acrostAb;
    internal AcrostSkill AcrostAb => _acrostAb ?? LazyInitializer.EnsureInitialized(ref _acrostAb, () => new AcrostSkill(this));

    internal AstralArrowSkill? _astralArrowAb;
    internal AstralArrowSkill AstralArrowAb => _astralArrowAb ?? LazyInitializer.EnsureInitialized(ref _astralArrowAb, () => new AstralArrowSkill(this));

    internal BladeMissileSkill? _bladeMissileAb;
    internal BladeMissileSkill BladeMissileAb => _bladeMissileAb ?? LazyInitializer.EnsureInitialized(ref _bladeMissileAb, () => new BladeMissileSkill(this));
    internal GravityFieldSkill? _gravityFieldAb;
    internal GravityFieldSkill GravityFieldAb => _gravityFieldAb ?? LazyInitializer.EnsureInitialized(ref _gravityFieldAb, () => new GravityFieldSkill(this));

    internal MeteorSkill? _meteorAb;
    internal MeteorSkill MeteorAb => _meteorAb ?? LazyInitializer.EnsureInitialized(ref _meteorAb, () => new MeteorSkill(this));

    internal HeavenEarthSkill? _heavenEarthAb;
    internal HeavenEarthSkill HeavenEarthAb => _heavenEarthAb ?? LazyInitializer.EnsureInitialized(ref _heavenEarthAb, () => new HeavenEarthSkill(this));

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
    internal BattleStats? _stats;
    internal BattleStats Stats => _stats ?? LazyInitializer.EnsureInitialized(ref _stats, () => new BattleStats(this));
    internal StatsScreen? _statsScr;
    internal StatsScreen StatsScr => _statsScr ?? LazyInitializer.EnsureInitialized(ref _statsScr, () => new StatsScreen(this));
    internal StatsConsentScreen? _consentScr;
    internal StatsConsentScreen ConsentScr => _consentScr ?? LazyInitializer.EnsureInitialized(ref _consentScr, () => new StatsConsentScreen(this));
    internal ReleaseNotesScreen? _notesScr;
    internal ReleaseNotesScreen NotesScr => _notesScr ?? LazyInitializer.EnsureInitialized(ref _notesScr, () => new ReleaseNotesScreen(this));
}
