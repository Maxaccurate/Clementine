using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace ZestDrop;

internal static class UiTheme
{
    public static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(36, 39, 43));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(110, 115, 123));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(213, 84, 24));
    public static void Apply(Window window)
    {
        window.Icon = AppIcon.Window;
        window.Resources = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#24272B"/>
            <Setter Property="BorderBrush" Value="#E0E2E5"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="14,9"/><Setter Property="MinHeight" Value="36"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.8"/><Setter TargetName="surface" Property="BorderBrush" Value="#A9AFB8"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#D55418"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="TextBox">
            <Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#DFE2E6"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="10,8"/><Setter Property="MinHeight" Value="36"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7"><ScrollViewer x:Name="PART_ContentHost"/></Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#D55418"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBox"><Setter Property="Padding" Value="9,7"/><Setter Property="MinHeight" Value="36"/><Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#DFE2E6"/></Style>
          <Style TargetType="PasswordBox"><Setter Property="Padding" Value="10,8"/><Setter Property="MinHeight" Value="36"/><Setter Property="BorderBrush" Value="#DFE2E6"/></Style>
          <Style TargetType="CheckBox"><Setter Property="Padding" Value="6,4"/><Setter Property="MinHeight" Value="30"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
          <Style TargetType="ListBox"><Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#E5E7EB"/><Setter Property="Padding" Value="4"/><Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/></Style>
          <Style TargetType="ListBoxItem"><Setter Property="Padding" Value="9,7"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="row" Background="Transparent" CornerRadius="6" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="row" Property="Background" Value="#FFF0E6"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="row" Property="Background" Value="#F2F3F5"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="DataGrid"><Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#E5E7EB"/><Setter Property="RowBackground" Value="White"/><Setter Property="AlternatingRowBackground" Value="#F8F9FA"/><Setter Property="GridLinesVisibility" Value="None"/><Setter Property="HeadersVisibility" Value="Column"/><Setter Property="RowHeight" Value="34"/><Setter Property="ColumnHeaderHeight" Value="34"/></Style>
          <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="#F2F3F5"/><Setter Property="Padding" Value="8"/><Setter Property="BorderThickness" Value="0"/></Style>
          <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#24272B"/></Style>
        </ResourceDictionary>
        """);
        window.FontFamily = new FontFamily("Microsoft YaHei UI");
        window.Foreground = Ink;
        window.FontSize = 13;
        window.Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));
        window.WindowStyle = WindowStyle.None;
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
        return root;
    }
    public static Border Card(UIElement content, Thickness padding) => new() { Child = content, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(230, 232, 235)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = padding };
}
