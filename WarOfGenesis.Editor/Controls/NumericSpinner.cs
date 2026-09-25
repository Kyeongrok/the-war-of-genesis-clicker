using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

// 사용자 지정 컨트롤의 기본 모양(Themes/Generic.xaml)을 이 어셈블리에서 찾게 한다 — App 리소스 없이 연 창에서도 모양이 선다.
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

namespace WarOfGenesis.Editor.Controls;

/// <summary>
/// 숫자 입력 + 위·아래 단추(누르고 있으면 되풀이) — mv-data-view 의 <c>MvDataView.Support.UI.Units.NumericSpinner</c> 를 옮겼다.
/// 휠·↑↓ 키도 <see cref="Step"/> 만큼 움직인다. 모양은 <c>Themes/Generic.xaml</c>.
/// </summary>
[TemplatePart(Name = PartTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PartUpButton, Type = typeof(ButtonBase))]
[TemplatePart(Name = PartDownButton, Type = typeof(ButtonBase))]
public class NumericSpinner : Control
{
    private const string PartTextBox = "PART_TextBox";
    private const string PartUpButton = "PART_UpButton";
    private const string PartDownButton = "PART_DownButton";

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericSpinner),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericSpinner),
            new PropertyMetadata(double.NegativeInfinity, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericSpinner),
            new PropertyMetadata(double.PositiveInfinity, OnRangeChanged));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumericSpinner), new PropertyMetadata(1.0));

    public static readonly DependencyProperty DecimalPlacesProperty =
        DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(NumericSpinner),
            new PropertyMetadata(0, OnDecimalPlacesChanged));

    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(NumericSpinner));

    public event RoutedPropertyChangedEventHandler<double> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public int DecimalPlaces { get => (int)GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }

    private TextBox? _textBox;
    private bool _isSyncing;

    static NumericSpinner() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NumericSpinner), new FrameworkPropertyMetadata(typeof(NumericSpinner)));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_textBox != null)
        {
            _textBox.LostFocus -= OnTextBoxLostFocus;
            _textBox.KeyDown -= OnTextBoxKeyDown;
            _textBox.PreviewMouseWheel -= OnMouseWheel;
        }
        _textBox = GetTemplateChild(PartTextBox) as TextBox;
        if (_textBox != null)
        {
            _textBox.Text = FormatValue(Value);
            _textBox.LostFocus += OnTextBoxLostFocus;
            _textBox.KeyDown += OnTextBoxKeyDown;
            _textBox.PreviewMouseWheel += OnMouseWheel;
        }
        if (GetTemplateChild(PartUpButton) is ButtonBase up) up.Click += (_, _) => StepBy(+Step);
        if (GetTemplateChild(PartDownButton) is ButtonBase down) down.Click += (_, _) => StepBy(-Step);
        PreviewMouseWheel -= OnMouseWheel;
        PreviewMouseWheel += OnMouseWheel;
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e) => CommitText();

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: CommitText(); e.Handled = true; break;
            case Key.Up: StepBy(+Step); e.Handled = true; break;
            case Key.Down: StepBy(-Step); e.Handled = true; break;
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        StepBy(e.Delta > 0 ? +Step : -Step);
        e.Handled = true;
    }

    private void CommitText()
    {
        if (_textBox == null) return;
        if (double.TryParse(_textBox.Text, out double v)) SetCurrentValue(ValueProperty, v);
        else SyncText();
    }

    private void StepBy(double delta) => SetCurrentValue(ValueProperty, Math.Round(Value + delta, DecimalPlaces));

    private void SyncText()
    {
        if (_textBox == null || _isSyncing) return;
        _isSyncing = true;
        _textBox.Text = FormatValue(Value);
        _isSyncing = false;
    }

    private string FormatValue(double v) => v.ToString(DecimalPlaces == 0 ? "F0" : $"F{DecimalPlaces}");

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (NumericSpinner)d;
        ctrl.SyncText();
        ctrl.RaiseEvent(new RoutedPropertyChangedEventArgs<double>((double)e.OldValue, (double)e.NewValue, ValueChangedEvent));
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var ctrl = (NumericSpinner)d;
        double v = Math.Max(ctrl.Minimum, Math.Min(ctrl.Maximum, (double)baseValue));
        return Math.Round(v, ctrl.DecimalPlaces);
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => d.CoerceValue(ValueProperty);

    private static void OnDecimalPlacesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (NumericSpinner)d;
        ctrl.CoerceValue(ValueProperty);
        ctrl.SyncText();
    }
}
