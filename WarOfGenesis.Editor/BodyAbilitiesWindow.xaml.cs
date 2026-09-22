using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 개발 > 체질 — 계열(체질)별로 직업이 <b>배울 수 있는 어빌리티</b>(<c>Dat/Job.dat</c> 레코드의 11칸)를 더하고 뺀다.
/// </summary>
/// <remarks>
/// 원본 규칙(분석-스킬·분석-체질): 배울 수 있는 목록 = 직업 레코드 파일 31~52 의 어빌리티 11칸 가운데 아직 없고 선행 조건을 채운 것.
/// 직업은 <c>Dep.dat</c> 계열 레코드(1~3 사이클론 · 4~6 타키리온 · 7~9 포스트럴 · 10~12 아크로스트 · 13~15 오즈마, 셋이 1·2·3단계)가
/// 품고 있다. 저장은 저장소 <c>assets/data/Dat/Job.dat</c> 에 한다 — 게임 폴더는 건드리지 않는다.
/// </remarks>
public partial class BodyAbilitiesWindow : Window
{
    private const int RecordSize = 67, SlotOffset = 31, SlotCount = 11, HeaderSize = 6;
    private static readonly string[] FormNames = ["일반형", "공격형", "방어형", "고속형", "보조형"];

    private sealed record JobRow(int Offset, int JobId, string Label);

    private sealed record SlotRow(int Index, int AbilityId, string Label);

    private sealed record AbilityRow(int Id, string Label);

    private readonly GameDatabase _db;
    private readonly string _path;
    private byte[] _bytes;
    private readonly List<JobRow> _jobs = [];
    private readonly List<AbilityRow> _abilities = [];
    private bool _dirty;

    public BodyAbilitiesWindow(GameDatabase db, string jobDatPath)
    {
        InitializeComponent();
        _db = db;
        _path = jobDatPath;
        _bytes = File.ReadAllBytes(_path);
        BuildJobs();
        foreach (var ab in db.Abilities.Values.OrderBy(a => a.Id))
            _abilities.Add(new AbilityRow(ab.Id, $"{ab.Id:D4} {db.T(ab.NameId)}{CategoryTag(ab.Category)}"));
        RefreshJobList();
        RefreshAbilityPicker();
        Status($"Job.dat 직업 {_jobs.Count}개 — {_path}");
    }

    private static string CategoryTag(byte category) => category switch
    {
        0 => " (비전투)",
        2 => " (군단기)",
        3 => " (패시브)",
        _ => "",
    };

    /// <summary>계열 → 단계 → 형 차례로 직업 줄을 만든다. 어느 계열에도 안 든 직업은 뒤에 「그 밖」으로.</summary>
    private void BuildJobs()
    {
        _jobs.Clear();
        var offsets = new Dictionary<int, int>();
        int count = BitConverter.ToUInt16(_bytes, 2);
        for (int i = 0, o = HeaderSize; i < count && o + RecordSize <= _bytes.Length; i++, o += RecordSize)
            offsets[BitConverter.ToUInt16(_bytes, o)] = o;

        var placed = new HashSet<int>();
        foreach (var dep in _db.Deps.Where(d => d.Id >= 1).OrderBy(d => d.Id))
        {
            int tier = (dep.Id - 1) % 3 + 1;
            string family = _db.T(dep.NameId);
            for (int k = 0; k < dep.Jobs.Length; k++)
            {
                int jobId = dep.Jobs[k];
                if (!offsets.TryGetValue(jobId, out int o) || !placed.Add(jobId)) continue;
                string form = k < FormNames.Length ? FormNames[k] : $"{k + 1}번째";
                _jobs.Add(new JobRow(o, jobId, $"{family} {tier}단계 · {form} — {JobName(jobId)} ({jobId})"));
            }
        }
        foreach (var (jobId, o) in offsets.OrderBy(p => p.Key))
            if (placed.Add(jobId)) _jobs.Add(new JobRow(o, jobId, $"그 밖 — {JobName(jobId)} ({jobId})"));
    }

    private string JobName(int jobId) =>
        _db.Jobs.TryGetValue(jobId, out var job) ? job.NamesByBody.Select(_db.T).FirstOrDefault(n => n.Length > 0) ?? $"직업 {jobId}" : $"직업 {jobId}";

    private void RefreshJobList()
    {
        string filter = JobFilterBox.Text.Trim();
        var selected = (JobList.SelectedItem as JobRow)?.JobId;
        JobList.ItemsSource = filter.Length == 0 ? _jobs.ToList()
            : _jobs.Where(j => j.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected is { } id) JobList.SelectedItem = ((List<JobRow>)JobList.ItemsSource).FirstOrDefault(j => j.JobId == id);
    }

    private void RefreshAbilityPicker()
    {
        string filter = AbilityFilterBox.Text.Trim();
        AbilityPicker.ItemsSource = filter.Length == 0 ? _abilities.ToList()
            : _abilities.Where(a => a.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (AbilityPicker.Items.Count > 0 && AbilityPicker.SelectedIndex < 0) AbilityPicker.SelectedIndex = 0;
    }

    private ushort[] SlotsOf(JobRow job) =>
        [.. Enumerable.Range(0, SlotCount).Select(k => BitConverter.ToUInt16(_bytes, job.Offset + SlotOffset + 2 * k))];

    private void WriteSlots(JobRow job, IReadOnlyList<ushort> slots)
    {
        for (int k = 0; k < SlotCount; k++)
            BitConverter.TryWriteBytes(_bytes.AsSpan(job.Offset + SlotOffset + 2 * k, 2), k < slots.Count ? slots[k] : (ushort)0);
        _dirty = true;
    }

    private void RefreshSlots()
    {
        if (JobList.SelectedItem is not JobRow job) { SlotList.ItemsSource = null; JobTitle.Text = "왼쪽에서 직업을 고르세요"; return; }
        var slots = SlotsOf(job);
        JobTitle.Text = job.Label;
        var rows = new List<SlotRow>();
        for (int k = 0; k < SlotCount; k++)
        {
            ushort id = slots[k];
            string label = id == 0 ? "(빈 칸)"
                : _db.Abilities.TryGetValue(id, out var ab)
                    ? $"{id:D4} {_db.T(ab.NameId)}{CategoryTag(ab.Category)}{Prereq(ab)}"
                    : $"{id:D4} (자료 없음)";
            rows.Add(new SlotRow(k, id, $"{k + 1,2}. {label}"));
        }
        SlotList.ItemsSource = rows;
    }

    private string Prereq(AbilityData ab)
    {
        var parts = new List<string>();
        if (ab.Prereq1 != 0 && _db.Abilities.TryGetValue(ab.Prereq1, out var p1)) parts.Add($"{_db.T(p1.NameId)} Lv{ab.Prereq1Level}");
        if (ab.Prereq2 != 0 && _db.Abilities.TryGetValue(ab.Prereq2, out var p2)) parts.Add($"{_db.T(p2.NameId)} Lv{ab.Prereq2Level}");
        return parts.Count == 0 ? "" : $" — 선행: {string.Join(", ", parts)}";
    }

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshSlots();

    private void JobFilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshJobList();

    private void AbilityFilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshAbilityPicker();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is not JobRow job) { Status("먼저 왼쪽에서 직업을 고르세요."); return; }
        if (AbilityPicker.SelectedItem is not AbilityRow pick) { Status("더할 어빌리티를 고르세요."); return; }
        var slots = SlotsOf(job).ToList();
        if (slots.Contains((ushort)pick.Id)) { Status($"{pick.Label} 은(는) 이미 목록에 있습니다."); return; }
        int empty = slots.IndexOf(0);
        if (empty < 0) { Status($"칸이 다 찼습니다 — 직업마다 {SlotCount}칸까지입니다. 하나를 지우고 더하세요."); return; }
        slots[empty] = (ushort)pick.Id;
        WriteSlots(job, slots);
        RefreshSlots();
        SlotList.SelectedIndex = empty;
        Status($"{empty + 1}번 칸에 {pick.Label} 을(를) 더했습니다. 저장을 눌러야 파일에 남습니다.");
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is not JobRow job) { Status("먼저 왼쪽에서 직업을 고르세요."); return; }
        if (SlotList.SelectedItem is not SlotRow slot || slot.AbilityId == 0) { Status("지울 어빌리티 칸을 고르세요."); return; }
        var slots = SlotsOf(job).ToList();
        slots.RemoveAt(slot.Index);            // 뒤 칸을 앞으로 당긴다 — 목록 차례가 곧 「배울 수 있는」 차례다
        slots.Add(0);
        WriteSlots(job, slots);
        RefreshSlots();
        Status($"{slot.Label.Trim()} 을(를) 뺐습니다. 저장을 눌러야 파일에 남습니다.");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string backup = _path + ".bak";
            if (!File.Exists(backup)) File.Copy(_path, backup);
            File.WriteAllBytes(_path, _bytes);
            _dirty = false;
            Status($"저장했습니다 — {_path} (처음 원본은 {Path.GetFileName(backup)} 에 남아 있습니다). 게임을 다시 켜면 반영됩니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status($"저장하지 못했습니다: {ex.Message}");
        }
    }

    private void RevertButton_Click(object sender, RoutedEventArgs e)
    {
        _bytes = File.ReadAllBytes(_path);
        _dirty = false;
        BuildJobs();
        RefreshJobList();
        RefreshSlots();
        Status("파일에 있는 대로 되돌렸습니다.");
    }

    private void Status(string text) => StatusText.Text = (_dirty ? "[저장 안 됨] " : "") + text;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_dirty && MessageBox.Show(this, "저장하지 않은 변경이 있습니다. 그냥 닫을까요?", "체질 어빌리티",
                                      MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
        base.OnClosing(e);
    }
}
