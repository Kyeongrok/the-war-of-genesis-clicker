namespace DuelDx;

/// <summary>창이 여는 장면들 — 필드(<see cref="FieldScene"/>)와 모세스(<see cref="MosesScene"/>)는 따로 선 클래스이고, 처음 쓸 때 만든다.</summary>
internal sealed unsafe partial class GameWindow
{
    /// <summary>FieldScene — 처음 쓸 때 만든다(장면마다 따로 선 클래스).</summary>
    internal FieldScene? _fldScene;
    internal FieldScene Fld => _fldScene ??= new FieldScene(this);

    /// <summary>필드가 열려 있나 — 장면 개체를 만들지 않고 본다(틀마다 여러 번 불린다).</summary>
    internal bool FieldOpen => _fldScene is { FieldOpen: true };

    /// <summary>MosesScene — 처음 쓸 때 만든다(장면마다 따로 선 클래스).</summary>
    internal MosesScene? _mosScene;
    internal MosesScene Mos => _mosScene ??= new MosesScene(this);
}
