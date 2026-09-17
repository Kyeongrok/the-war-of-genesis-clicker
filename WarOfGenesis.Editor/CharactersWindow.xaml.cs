using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 인물 전체를 <b>id · 이름 · 초상</b> 한 줄씩 늘어놓고 보는 창 — 이름표(TXR) 짝이
/// 맞게 붙었는지 눈으로 훑어보려고 만들었다.
/// </summary>
/// <remarks>
/// 초상은 겹치는 인물이 많아(같은 얼굴을 여럿이 쓴다) <b>face 번호로 한 번만</b> 풀고
/// 그 결과를 겹치는 줄에 다 같이 붙인다 — 576명을 다 훑어도 배경 스레드에서 face
/// 번호 가짓수만큼만 파일을 연다.
/// </remarks>
public partial class CharactersWindow : Window
{
    private readonly string _obsFolder;
    private readonly ObservableCollection<CharacterRow> _all = [];
    private readonly ICollectionView _view;

    public CharactersWindow(IReadOnlyList<ChrRecord> records, TxrTable txr, string obsFolder)
    {
        InitializeComponent();
        _obsFolder = obsFolder;

        foreach (var r in records.OrderBy(r => r.ChrCode))
        {
            string name = txr.TextOf(r.NameCode);
            if (name.Length == 0) name = txr.TextOf(r.AltNameCode);
            _all.Add(new CharacterRow
            {
                ChrCode = r.ChrCode,
                Name = name,
                SpriteCode = r.SpriteCode,
                FaceCode = r.FaceCode,
                JobCode = r.JobCode,
                File = r.File,
            });
        }

        Grid.ItemsSource = _all;
        _view = CollectionViewSource.GetDefaultView(_all);
        StatusText.Text = $"{_all.Count}개";

        LoadPortraits();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = FilterBox.Text.Trim();
        _view.Filter = q.Length == 0 ? null : (Predicate<object>)(o =>
        {
            var row = (CharacterRow)o;
            if (int.TryParse(q, out int n)) return row.ChrCode == n || row.SpriteCode == n || row.FaceCode == n;
            return row.Name.Contains(q, StringComparison.Ordinal);
        });
    }

    /// <summary>face 번호가 같은 줄끼리 묶어, 겹치는 얼굴은 한 번만 푼다.</summary>
    private void LoadPortraits()
    {
        var byFace = _all.Where(r => r.FaceCode != 0).GroupBy(r => r.FaceCode).ToList();
        int total = byFace.Count, done = 0;

        Task.Run(() =>
        {
            foreach (var group in byFace)
            {
                ImageSource? image = DecodePortrait(group.Key);
                done++;
                int doneNow = done;
                Dispatcher.BeginInvoke(() =>
                {
                    foreach (var row in group) row.Portrait = image;
                    StatusText.Text = $"{_all.Count}개 — 초상 {doneNow}/{total}";
                });
            }
        });
    }

    private ImageSource? DecodePortrait(int faceCode)
    {
        try
        {
            string obsName = CharacterExport.ObsFileName(faceCode);
            if (!PakArchive.EnsureFile(_obsFolder, "Obs", obsName)) return null;

            var frame = ObsSprite.DecodeFirstFrame(Path.Combine(_obsFolder, obsName));
            if (frame == null) return null;

            var bmp = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32,
                                          null, frame.Bgra, frame.Width * 4);
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return null;
        }
    }
}

/// <summary>목록의 한 줄. 초상은 나중에 채워지므로 <see cref="INotifyPropertyChanged"/> 로 알린다.</summary>
public sealed class CharacterRow : INotifyPropertyChanged
{
    public required int ChrCode { get; init; }
    public required string Name { get; init; }
    public required int SpriteCode { get; init; }
    public required int FaceCode { get; init; }
    public required int JobCode { get; init; }
    public required string File { get; init; }

    private ImageSource? _portrait;
    public ImageSource? Portrait
    {
        get => _portrait;
        set { _portrait = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
