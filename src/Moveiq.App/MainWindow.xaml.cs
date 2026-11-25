using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Moveiq.Input;

namespace Moveiq;

public partial class MainWindow : Window
{
    private static readonly (string First, string Second, string Name)[] ComboPairs =
    [
        ("Back", "Right", "Back + Right click"),
        ("Back", "Left", "Back + Left click"),
        ("Forward", "Right", "Forward + Right click"),
        ("Forward", "Left", "Forward + Left click"),
        ("Back", "Forward", "Back + Forward")
    ];

    private readonly ObservableCollection<InputEventRow> _events = [];
    private readonly HashSet<string> _pendingButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MouseAction> _pendingSingleActions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumedUntilRelease = new(StringComparer.Ordinal);
    private readonly Queue<double> _movementIntervals = new();
    private MouseHook? _hook;
    private bool _mappingsEnabled, _movementTelemetryEnabled, _hasLastMovePoint;
    private long _buttonPressCount, _sideButtonPressCount, _verticalWheelCount, _horizontalWheelCount;
    private long _movementEventCount, _lastMoveTimestamp, _lastChartRefreshTimestamp;
    private int _lastMoveX, _lastMoveY;
    private double? _latestMovementInterval;

    public MainWindow()
    {
        InitializeComponent();
        EventsList.ItemsSource = _events;
        AppButtonBox.ItemsSource = new[] { "Back side button", "Forward side button", "Scroll wheel click" };
        AppButtonBox.SelectedIndex = 0;
        foreach (var box in SingleActionBoxes())
        {
            box.ItemsSource = ActionOptions.All;
            box.SelectedItem = ActionOptions.All[0];
        }
        foreach (var box in ComboActionBoxes())
        {
            box.ItemsSource = ComboActionOptions.All;
            box.SelectedItem = ComboActionOptions.All[0];
        }
    }

    private ComboBox[] SingleActionBoxes() => [BackActionBox, ForwardActionBox, MiddleActionBox];
    private ComboBox[] ComboActionBoxes() => [ComboAction1, ComboAction2, ComboAction3, ComboAction4, ComboAction5];

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _movementTelemetryEnabled = true;
        try
        {
            EnsureHook();
            MovementStatusText.Text = "LIVE · Move the mouse to see Windows pointer updates.";
        }
        catch (Exception ex)
        {
            MovementStatusText.Text = $"Live movement reading unavailable: {ex.Message}";
        }
    }

    private void ApplyMappings_Click(object sender, RoutedEventArgs e)
    {
        if (_mappingsEnabled)
        {
            DisableMappings();
            return;
        }

        if (!HasAnyMappings())
        {
            StatusText.Text = "Choose an action on the left or right first.";
            return;
        }

        if (UsesSelectedAppAction() && string.IsNullOrWhiteSpace(_buttonAppPath))
        {
            StatusText.Text = "Choose an app first for the ‘Open selected app’ action.";
            return;
        }

        try
        {
            EnsureHook();
            _mappingsEnabled = true;
            RefreshMappingStatus();
        }
        catch (Exception ex)
        {
            StatusText.Text = "ToadIQ could not start. Your mouse is unchanged.";
            MessageBox.Show(this, ex.Message, "ToadIQ could not start", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool HasAnyMappings() => SingleActionBoxes().Any(box => SelectedAction(box) != MouseAction.PassThrough)
        || ComboActionBoxes().Any(box => SelectedAction(box) != MouseAction.PassThrough);

    private string? _buttonAppPath;

    private bool UsesSelectedAppAction() => SingleActionBoxes().Any(box => SelectedAction(box) == MouseAction.LaunchApp)
        || ComboActionBoxes().Any(box => SelectedAction(box) == MouseAction.LaunchApp);

    private void DisableMappings()
    {
        foreach (var button in _pendingButtons.ToArray())
            KeyboardActionSender.ReplayMouseClick(button);
        _pendingButtons.Clear();
        _pendingSingleActions.Clear();
        _consumedUntilRelease.Clear();
        _mappingsEnabled = false;
        RefreshMappingStatus();
        StopHookIfUnused();
    }

    private void RefreshMappingStatus()
    {
        if (_mappingsEnabled)
        {
            StatusText.Text = "ON · Your selected button actions are active.";
            ApplyMappingsButton.Content = "Turn off custom buttons";
            ApplyMappingsButton.Background = new SolidColorBrush(Color.FromRgb(176, 76, 58));
            ApplyMappingsButton.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(176, 76, 58), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.35
            };
        }
        else
        {
            StatusText.Text = "OFF · Your mouse works normally.";
            ApplyMappingsButton.Content = "Turn on custom buttons";
            ApplyMappingsButton.Background = (Brush)FindResource("Accent");
            ApplyMappingsButton.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(22, 138, 112), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.48
            };
        }
    }

    private void MainPageScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || !IsInsideList(e.OriginalSource as DependencyObject)) return;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static bool IsInsideList(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ListBox or ListView) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void Action_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_mappingsEnabled) RefreshMappingStatus();
    }

    private static MouseAction SelectedAction(ComboBox box) => box.SelectedItem is ActionOption option
        ? option.Action
        : MouseAction.PassThrough;

    private MouseAction SingleAction(string button) => button switch
    {
        "Back" => SelectedAction(BackActionBox),
        "Forward" => SelectedAction(ForwardActionBox),
        "Middle" => SelectedAction(MiddleActionBox),
        _ => MouseAction.PassThrough
    };

    private MouseAction ComboAction(int index) => SelectedAction(ComboActionBoxes()[index]);

    private void EnsureHook()
    {
        if (_hook is null) _hook = new MouseHook(OnMouseEvent);
    }

    private void Diagnostics_Expanded(object sender, RoutedEventArgs e)
    {
        try { EnsureHook(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not show mouse activity", MessageBoxButton.OK, MessageBoxImage.Warning);
            DiagnosticsExpander.IsExpanded = false;
        }
    }

    private void Diagnostics_Collapsed(object sender, RoutedEventArgs e) => StopHookIfUnused();

    private void StopHookIfUnused()
    {
        if (_mappingsEnabled || _movementTelemetryEnabled || DiagnosticsExpander.IsExpanded || _hook is null) return;
        _hook.Dispose();
        _hook = null;
    }

    private bool OnMouseEvent(MouseEventInfo info)
    {
        if (info.Name == "Mouse move")
        {
            ObserveMovement(info);
            return false;
        }

        if (info.Name.EndsWith("down", StringComparison.OrdinalIgnoreCase))
        {
            _buttonPressCount++;
            if (info.Name == "X button down") _sideButtonPressCount++;
        }
        else if (info.Name == "Wheel") _verticalWheelCount++;
        else if (info.Name == "Horizontal wheel") _horizontalWheelCount++;

        Dispatcher.BeginInvoke(() =>
        {
            ButtonPressCountText.Text = _buttonPressCount.ToString("N0");
            SideButtonPressCountText.Text = _sideButtonPressCount.ToString("N0");
            VerticalWheelCountText.Text = _verticalWheelCount.ToString("N0");
            HorizontalWheelCountText.Text = _horizontalWheelCount.ToString("N0");
            ActivityLastEventText.Text = $"Last event: {info.Name} · {info.Time:HH:mm:ss}";
            if (DiagnosticsExpander.IsExpanded)
            {
                if (info.Name != "Mouse move")
                {
                    _events.Insert(0, new InputEventRow(info.Time.ToString("HH:mm:ss.fff"), info.Name, info.Details));
                    while (_events.Count > 300) _events.RemoveAt(_events.Count - 1);
                }
            }
        });

        if (!_mappingsEnabled || !TryGetButtonEvent(info, out var button, out var down)) return false;
        return HandleMappedButton(button, down);
    }

    private void ObserveMovement(MouseEventInfo info)
    {
        _movementEventCount++;
        if (_lastMoveTimestamp > 0)
        {
            var interval = (info.Timestamp - _lastMoveTimestamp) * 1000d / Stopwatch.Frequency;
            if (double.IsFinite(interval) && interval >= 0)
            {
                _latestMovementInterval = interval;
                _movementIntervals.Enqueue(interval);
                while (_movementIntervals.Count > 80) _movementIntervals.Dequeue();
            }
        }

        var deltaX = _hasLastMovePoint ? info.X - _lastMoveX : 0;
        var deltaY = _hasLastMovePoint ? info.Y - _lastMoveY : 0;
        _lastMoveTimestamp = info.Timestamp;
        _lastMoveX = info.X;
        _lastMoveY = info.Y;
        _hasLastMovePoint = true;

        if (info.Timestamp - _lastChartRefreshTimestamp < Stopwatch.Frequency / 30) return;
        _lastChartRefreshTimestamp = info.Timestamp;
        var eventCount = _movementEventCount;
        var latestInterval = _latestMovementInterval;
        var intervals = _movementIntervals.ToArray();
        Dispatcher.BeginInvoke(() =>
        {
            MovementIntervalText.Text = latestInterval is { } value ? $"{value:0.00} ms" : "Waiting…";
            MovementEventCountText.Text = $"{eventCount:N0} movement events";
            MovementDeltaText.Text = $"Latest pointer shift: {deltaX:+#;-#;0}, {deltaY:+#;-#;0} px";
            MovementStatusText.Text = "LIVE · Windows pointer movement observed.";
            DrawMovementTrace(intervals);
        });
    }

    private void MovementChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMovementTrace(_movementIntervals.ToArray());

    private void DrawMovementTrace(IReadOnlyList<double> intervals)
    {
        var width = MovementChartCanvas.ActualWidth;
        var height = MovementChartCanvas.ActualHeight;
        if (width <= 0 || height <= 0 || intervals.Count == 0) return;

        var points = new PointCollection();
        var first = Math.Max(0, intervals.Count - 80);
        var count = intervals.Count - first;
        for (var index = 0; index < count; index++)
        {
            var value = Math.Clamp(intervals[first + index], 0, 20);
            var x = count == 1 ? width : index * width / (count - 1d);
            var y = height - value * height / 20d;
            points.Add(new Point(x, y));
        }
        MovementTrace.Points = points;
    }

    private bool HandleMappedButton(string button, bool down)
    {
        if (!down)
        {
            if (_consumedUntilRelease.Remove(button)) return true;
            if (!_pendingButtons.Remove(button)) return false;
            var actionOnRelease = _pendingSingleActions.Remove(button, out var selected) ? selected : MouseAction.PassThrough;
            if (actionOnRelease != MouseAction.PassThrough) Dispatch(actionOnRelease);
            else KeyboardActionSender.ReplayMouseClick(button);
            return true;
        }

        if (_consumedUntilRelease.Contains(button) || _pendingButtons.Contains(button)) return true;
        var activeComboIndex = FindCombo(button);
        var singleAction = SingleAction(button);
        if (activeComboIndex < 0 && singleAction == MouseAction.PassThrough) return false;

        _pendingButtons.Add(button);
        _pendingSingleActions[button] = singleAction;
        if (activeComboIndex < 0) return true;

        var pair = ComboPairs[activeComboIndex];
        var other = pair.First == button ? pair.Second : pair.First;
        if (!_pendingButtons.Contains(other)) return true;

        _pendingButtons.Remove(button);
        _pendingButtons.Remove(other);
        _pendingSingleActions.Remove(button);
        _pendingSingleActions.Remove(other);
        _consumedUntilRelease.Add(button);
        _consumedUntilRelease.Add(other);
        Dispatch(ComboAction(activeComboIndex));
        return true;
    }

    private int FindCombo(string button)
    {
        for (var index = 0; index < ComboPairs.Length; index++)
        {
            var pair = ComboPairs[index];
            if (ComboAction(index) != MouseAction.PassThrough && (pair.First == button || pair.Second == button))
                return index;
        }
        return -1;
    }

    private void Dispatch(MouseAction action)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (action == MouseAction.LaunchApp)
                {
                    if (string.IsNullOrWhiteSpace(_buttonAppPath))
                    {
                        StatusText.Text = "Choose an app first for the ‘Open selected app’ action.";
                        return;
                    }
                    Process.Start(new ProcessStartInfo(_buttonAppPath) { UseShellExecute = true });
                    return;
                }
                KeyboardActionSender.Send(action);
            }
            catch (Exception ex)
            {
                DisableMappings();
                MessageBox.Show(this, $"ToadIQ turned off custom buttons because it could not run the selected action.\n\n{ex.Message}", "ToadIQ", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private static bool TryGetButtonEvent(MouseEventInfo info, out string button, out bool down)
    {
        down = info.Name.EndsWith("down", StringComparison.OrdinalIgnoreCase);
        button = info.Name switch
        {
            "Left button down" or "Left button up" => "Left",
            "Right button down" or "Right button up" => "Right",
            "Middle button down" or "Middle button up" => "Middle",
            "X button down" or "X button up" => info.Details.StartsWith("XBUTTON1", StringComparison.Ordinal) ? "Back" : "Forward",
            _ => string.Empty
        };
        return button.Length > 0;
    }

    private void ChooseButtonApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the app for your mouse button", Filter = "Application files (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _buttonAppPath = dialog.FileName;
        ButtonAppText.Text = System.IO.Path.GetFileName(_buttonAppPath);
        ButtonAppText.ToolTip = _buttonAppPath;
    }

    private void MouseHotspot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string button }) return;
        AppButtonBox.SelectedIndex = button switch
        {
            "Back" => 0,
            "Forward" => 1,
            "Middle" => 2,
            _ => -1
        };
    }

    private void AppButton_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackActionBox is null) return;
        (Button Hotspot, ComboBox Box, string Name)? selected = AppButtonBox.SelectedIndex switch
        {
            0 => (HotspotBack, BackActionBox, "Back side button"),
            1 => (HotspotForward, ForwardActionBox, "Forward side button"),
            2 => (HotspotWheel, MiddleActionBox, "Scroll wheel click"),
            _ => null
        };

        foreach (var hotspot in new[] { HotspotBack, HotspotForward, HotspotWheel })
        {
            var isForwardOrWheel = hotspot == HotspotForward || hotspot == HotspotWheel;
            hotspot.Background = new SolidColorBrush(isForwardOrWheel ? Color.FromRgb(7, 83, 91) : Color.FromRgb(21, 53, 45));
            hotspot.BorderBrush = (Brush)FindResource(isForwardOrWheel ? "Secondary" : "Accent");
        }
        foreach (var box in SingleActionBoxes()) box.BorderBrush = (Brush)FindResource("Line");

        if (selected is not { } target) return;
        target.Hotspot.Background = new SolidColorBrush(Color.FromRgb(64, 38, 92));
        target.Hotspot.BorderBrush = (Brush)FindResource("Tertiary");
        target.Box.BorderBrush = (Brush)FindResource("Tertiary");
    }

    private void AssignAppToButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_buttonAppPath))
        {
            StatusText.Text = "Choose an application first.";
            return;
        }

        var target = AppButtonBox.SelectedIndex switch
        {
            0 => BackActionBox,
            1 => ForwardActionBox,
            2 => MiddleActionBox,
            _ => null
        };
        if (target is null) return;

        target.SelectedItem = ActionOptions.All.First(option => option.Action == MouseAction.LaunchApp);
        StatusText.Text = $"{AppButtonBox.SelectedItem} will open {System.IO.Path.GetFileName(_buttonAppPath)} when custom buttons are on.";
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _events.Clear();

    protected override void OnClosed(EventArgs e)
    {
        _hook?.Dispose();
        base.OnClosed(e);
    }

}

public sealed record InputEventRow(string Time, string Name, string Details);
public sealed record ActionOption(string Name, MouseAction Action)
{
    public override string ToString() => Name;
}

public static class ActionOptions
{
    public static readonly ActionOption[] All =
    [
        new("Keep normal action", MouseAction.PassThrough),
        new("Take a screenshot", MouseAction.Screenshot),
        new("Copy selected text", MouseAction.Copy),
        new("Paste copied text", MouseAction.Paste),
        new("Undo the last change", MouseAction.Undo),
        new("Redo the last change", MouseAction.Redo),
        new("Switch between open apps", MouseAction.TaskSwitcher),
        new("Open clipboard history", MouseAction.ClipboardHistory),
        new("Go back a page", MouseAction.BrowserBack),
        new("Go forward a page", MouseAction.BrowserForward),
        new("Open selected app", MouseAction.LaunchApp)
    ];
}

public static class ComboActionOptions
{
    public static readonly ActionOption[] All = [new("Off", MouseAction.PassThrough), .. ActionOptions.All.Skip(1)];
}
