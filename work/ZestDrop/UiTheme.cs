using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace ZestDrop;

internal static class UiTheme
{
    public const double WindowCornerRadius = 20;
    private static readonly Uri FontBase = new($"pack://application:,,,/{typeof(UiTheme).Assembly.GetName().Name};component/");
    public static readonly FontFamily ChineseFont = new(FontBase, "./assets/fonts/#Source Han Sans CN Medium");
    // Latin uses Segoe UI; Chinese resolves to the bundled physical Medium face.
    public static readonly FontFamily Font = new(FontBase, "Segoe UI, ./assets/fonts/#Source Han Sans CN Medium");

    // --- light and dark ------------------------------------------------------------------------------------------
    // "system" follows the Windows app colour (Settings → Personalisation → Colours); "light" and "dark" fix it.
    // Windows take the theme when they open; the wheel and the progress cards follow a change at once.
    public const string ThemeKey = "theme";
    private sealed record Palette(string Window, string Surface, string Ink, string Muted, string Line, string StrongLine, string Hover, string Tint, string Rail, string Alt, string Preview);
    private static readonly Palette LightPalette = new("#F8F9FA", "#FFFFFF", "#24272B", "#6E737B", "#E3E5E9", "#A9AFB8", "#F1F3F5", "#FFF0E6", "#E5E8ED", "#F8F9FA", "#F4F5F7");
    private static readonly Palette DarkPalette = new("#1C1E22", "#26292E", "#E6E8EB", "#9EA4AC", "#3A3F46", "#5E656F", "#30343A", "#4A2D1D", "#454B53", "#2B2E33", "#202327");
    private static Palette palette = LightPalette;
    public static bool Dark { get; private set; }
    public static string Choice => Settings.GetString(ThemeKey) is "light" or "dark" ? Settings.GetString(ThemeKey)! : "system";
    public static Brush Ink { get; private set; } = Brushes.Black;
    public static Brush Muted { get; private set; } = Brushes.Gray;
    public static readonly Brush Accent = Frozen("#D55418");
    public static Brush WindowBack { get; private set; } = Brushes.White;
    public static Brush Surface { get; private set; } = Brushes.White;
    public static Brush Line { get; private set; } = Brushes.LightGray;
    public static Brush Tint { get; private set; } = Brushes.White;
    public static Brush PreviewBack { get; private set; } = Brushes.White;
    public static Color SurfaceColor => (Color)ColorConverter.ConvertFromString(palette.Surface);
    public static Color LineColor => (Color)ColorConverter.ConvertFromString(palette.Line);

    static UiTheme() => Refresh();

    // Reads the chosen theme (and, for "system", the Windows setting) again.
    public static void Refresh()
    {
        string choice = Choice;
        Dark = choice == "dark" || choice == "system" && SystemUsesDark();
        palette = Dark ? DarkPalette : LightPalette;
        Ink = Frozen(palette.Ink); Muted = Frozen(palette.Muted); WindowBack = Frozen(palette.Window); Surface = Frozen(palette.Surface);
        Line = Frozen(palette.Line); Tint = Frozen(palette.Tint); PreviewBack = Frozen(palette.Preview);
    }

    public static void Choose(string choice) { Settings.Set(ThemeKey, choice); Refresh(); }

    private static bool SystemUsesDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    // Puts the palette's colours into a style text that names them as {Ink}, {Surface} and so on.
    public static string Fill(string xaml) => xaml.Replace("{Surface}", palette.Surface).Replace("{Ink}", palette.Ink).Replace("{Muted}", palette.Muted)
        .Replace("{StrongLine}", palette.StrongLine).Replace("{Line}", palette.Line).Replace("{Hover}", palette.Hover).Replace("{Tint}", palette.Tint)
        .Replace("{Rail}", palette.Rail).Replace("{Alt}", palette.Alt);

    public static void Apply(Window window)
    {
        Refresh();
        window.Icon = AppIcon.Window;
        window.Resources = (ResourceDictionary)XamlReader.Parse(Fill("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:p="clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework">
          <Style TargetType="Button">
            <Setter Property="Background" Value="{Surface}"/><Setter Property="Foreground" Value="{Ink}"/>
            <Setter Property="BorderBrush" Value="{Line}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="14,9"/><Setter Property="MinHeight" Value="36"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.8"/><Setter TargetName="surface" Property="BorderBrush" Value="{StrongLine}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#D55418"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="TextBox">
            <Setter Property="Background" Value="{Surface}"/><Setter Property="Foreground" Value="{Ink}"/><Setter Property="CaretBrush" Value="{Ink}"/><Setter Property="BorderBrush" Value="{Line}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="10,8"/><Setter Property="MinHeight" Value="36"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7"><ScrollViewer x:Name="PART_ContentHost"/></Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#D55418"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBoxItem">
            <Setter Property="Foreground" Value="{Ink}"/><Setter Property="Padding" Value="10,9"/>
            <Setter Property="MinHeight" Value="36"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
              <Border x:Name="row" Background="Transparent" CornerRadius="6" Padding="{TemplateBinding Padding}" Margin="0,1">
                <Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="22"/></Grid.ColumnDefinitions>
                  <ContentPresenter VerticalAlignment="Center" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/>
                  <Path x:Name="check" Grid.Column="1" Data="M 1,5 L 4,8 L 10,1" Stroke="#D55418" StrokeThickness="1.7" StrokeStartLineCap="Round" StrokeEndLineCap="Round" HorizontalAlignment="Right" VerticalAlignment="Center" Visibility="Collapsed"/>
                </Grid>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="row" Property="Background" Value="{Hover}"/></Trigger>
                <Trigger Property="IsSelected" Value="True"><Setter TargetName="row" Property="Background" Value="{Tint}"/><Setter TargetName="check" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBox">
            <Setter Property="Padding" Value="11,8"/><Setter Property="MinHeight" Value="38"/>
            <Setter Property="Background" Value="{Surface}"/><Setter Property="Foreground" Value="{Ink}"/>
            <Setter Property="BorderBrush" Value="{Line}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="MaxDropDownHeight" Value="320"/><Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
              <Grid SnapsToDevicePixels="True">
                <Border x:Name="surface" CornerRadius="8" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"/>
                <p:ToggleButton x:Name="toggle" Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                  <p:ToggleButton.Template><ControlTemplate TargetType="p:ToggleButton"><Border Background="Transparent" CornerRadius="8"/></ControlTemplate></p:ToggleButton.Template>
                </p:ToggleButton>
                <ContentPresenter x:Name="selection" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" Margin="11,8,34,8" VerticalAlignment="Center" HorizontalAlignment="Left" IsHitTestVisible="False"/>
                <TextBox x:Name="PART_EditableTextBox" Visibility="Collapsed" Margin="11,4,34,4" VerticalContentAlignment="Center" IsReadOnly="{TemplateBinding IsReadOnly}" Background="Transparent" Foreground="{TemplateBinding Foreground}">
                  <TextBox.Template><ControlTemplate TargetType="TextBox"><ScrollViewer x:Name="PART_ContentHost"/></ControlTemplate></TextBox.Template>
                </TextBox>
                <Path x:Name="chevron" Data="M 0,0 L 4,4 L 8,0" Stroke="{Muted}" StrokeThickness="1.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,14,0" IsHitTestVisible="False"/>
                <p:Popup x:Name="PART_Popup" Placement="Bottom" HorizontalOffset="-8" VerticalOffset="-4" AllowsTransparency="True" Focusable="False" IsOpen="{TemplateBinding IsDropDownOpen}" PopupAnimation="Fade">
                  <Border Margin="8" Padding="4" CornerRadius="9" Background="{Surface}" BorderBrush="{Line}" BorderThickness="1" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                    <Border.Effect><DropShadowEffect BlurRadius="12" ShadowDepth="3" Opacity="0.12" Color="#202832"/></Border.Effect>
                    <ScrollViewer MaxHeight="{TemplateBinding MaxDropDownHeight}" CanContentScroll="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                      <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                    </ScrollViewer>
                  </Border>
                </p:Popup>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="{StrongLine}"/><Setter TargetName="surface" Property="Background" Value="{Surface}"/></Trigger>
                <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#D55418"/></Trigger>
                <Trigger Property="IsDropDownOpen" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#D55418"/><Setter TargetName="chevron" Property="Stroke" Value="#D55418"/></Trigger>
                <Trigger Property="IsEditable" Value="True"><Setter TargetName="selection" Property="Visibility" Value="Collapsed"/><Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="SliderRail" TargetType="p:RepeatButton">
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="p:RepeatButton">
              <Border x:Name="rail" Height="4" VerticalAlignment="Center" Background="{TemplateBinding Background}" CornerRadius="2"/>
              <ControlTemplate.Triggers><DataTrigger Binding="{Binding Orientation, RelativeSource={RelativeSource AncestorType=Slider}}" Value="Vertical">
                <Setter TargetName="rail" Property="Height" Value="Auto"/><Setter TargetName="rail" Property="Width" Value="4"/><Setter TargetName="rail" Property="VerticalAlignment" Value="Stretch"/><Setter TargetName="rail" Property="HorizontalAlignment" Value="Center"/>
              </DataTrigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="SliderHandle" TargetType="p:Thumb">
            <Setter Property="Width" Value="24"/><Setter Property="Height" Value="24"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="p:Thumb">
              <Grid Background="Transparent">
                <Ellipse x:Name="focus" Width="24" Height="24" Fill="#24D55418" Visibility="Collapsed"/>
                <Ellipse x:Name="knob" Width="16" Height="16" Fill="{Surface}" Stroke="#D55418" StrokeThickness="2">
                  <Ellipse.Effect><DropShadowEffect BlurRadius="4" ShadowDepth="1" Opacity="0.12" Color="#39414C"/></Ellipse.Effect>
                </Ellipse>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="knob" Property="Fill" Value="{Tint}"/></Trigger>
                <Trigger Property="IsDragging" Value="True"><Setter TargetName="knob" Property="Fill" Value="#D55418"/><Setter TargetName="focus" Property="Visibility" Value="Visible"/></Trigger>
                <DataTrigger Binding="{Binding IsKeyboardFocusWithin, RelativeSource={RelativeSource AncestorType=Slider}}" Value="True"><Setter TargetName="focus" Property="Visibility" Value="Visible"/></DataTrigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Slider">
            <Setter Property="MinHeight" Value="28"/><Setter Property="Foreground" Value="#D55418"/>
            <Setter Property="IsMoveToPointEnabled" Value="True"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
              <Grid Background="{TemplateBinding Background}" SnapsToDevicePixels="True">
                <p:Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="{TemplateBinding IsDirectionReversed}">
                  <p:Track.DecreaseRepeatButton><p:RepeatButton Style="{StaticResource SliderRail}" Background="{TemplateBinding Foreground}" Command="Slider.DecreaseLarge"/></p:Track.DecreaseRepeatButton>
                  <p:Track.IncreaseRepeatButton><p:RepeatButton Style="{StaticResource SliderRail}" Background="{Rail}" Command="Slider.IncreaseLarge"/></p:Track.IncreaseRepeatButton>
                  <p:Track.Thumb><p:Thumb Style="{StaticResource SliderHandle}"/></p:Track.Thumb>
                </p:Track>
              </Grid>
              <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="PasswordBox"><Setter Property="Padding" Value="10,8"/><Setter Property="MinHeight" Value="36"/><Setter Property="Background" Value="{Surface}"/><Setter Property="Foreground" Value="{Ink}"/><Setter Property="CaretBrush" Value="{Ink}"/><Setter Property="BorderBrush" Value="{Line}"/></Style>
          <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{Ink}"/><Setter Property="Padding" Value="6,4"/><Setter Property="MinHeight" Value="30"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
          <Style TargetType="ListBox"><Setter Property="Background" Value="{Surface}"/><Setter Property="Foreground" Value="{Ink}"/><Setter Property="BorderBrush" Value="{Line}"/><Setter Property="Padding" Value="4"/><Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/></Style>
          <Style TargetType="ListBoxItem"><Setter Property="Padding" Value="9,7"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="row" Background="Transparent" CornerRadius="6" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="row" Property="Background" Value="{Tint}"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="row" Property="Background" Value="{Hover}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="DataGrid"><Setter Property="Background" Value="{Surface}"/><Setter Property="Foreground" Value="{Ink}"/><Setter Property="BorderBrush" Value="{Line}"/><Setter Property="RowBackground" Value="{Surface}"/><Setter Property="AlternatingRowBackground" Value="{Alt}"/><Setter Property="GridLinesVisibility" Value="None"/><Setter Property="HeadersVisibility" Value="Column"/><Setter Property="RowHeight" Value="34"/><Setter Property="ColumnHeaderHeight" Value="34"/></Style>
          <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="{Hover}"/><Setter Property="Foreground" Value="{Ink}"/><Setter Property="Padding" Value="8"/><Setter Property="BorderThickness" Value="0"/></Style>
          <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{Ink}"/></Style>
          <Style TargetType="Expander"><Setter Property="Foreground" Value="{Ink}"/></Style>
          <Style TargetType="ContextMenu"><Setter Property="Background" Value="{Surface}"/><Setter Property="Foreground" Value="{Ink}"/><Setter Property="BorderBrush" Value="{Line}"/></Style>
        </ResourceDictionary>
        """));
        window.FontFamily = Font;
        window.Foreground = Ink;
        window.FontSize = 13;
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        // A native window region cuts pixels into steps. Let WPF composite the rounded alpha edge instead.
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 42, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
    }
    public static UIElement Frame(Window window, UIElement content, string? title = null)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition());
        var caption = new DockPanel { Margin = new Thickness(24, 0, 8, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right);
        var minimize = new Button { Content = L.T("最小化"), FontSize = 11, Padding = new Thickness(12, 4, 12, 4), MinHeight = 28, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        minimize.Click += (_, _) => window.WindowState = WindowState.Minimized;
        var close = new Button { Content = L.T("关闭"), FontSize = 11, Padding = new Thickness(12, 4, 12, 4), MinHeight = 28, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => window.Close();
        WindowChrome.SetIsHitTestVisibleInChrome(actions, true);
        actions.Children.Add(minimize);
        actions.Children.Add(close);
        caption.Children.Add(actions);
        caption.Children.Add(new TextBlock { Text = title ?? L.T("ZestDrop  /  工具"), Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(caption);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        var surface = new ContinuousFrame { Child = root, CornerRadius = WindowCornerRadius };
        void UpdateCorners() => surface.CornerRadius = window.WindowState == WindowState.Maximized ? 0 : WindowCornerRadius;
        EventHandler changed = (_, _) => UpdateCorners();
        surface.Loaded += (_, _) => { window.StateChanged += changed; UpdateCorners(); };
        surface.Unloaded += (_, _) => window.StateChanged -= changed;
        UpdateCorners();
        return surface;
    }
    public static Border Card(UIElement content, Thickness padding) => new() { Child = content, Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = padding };
}
