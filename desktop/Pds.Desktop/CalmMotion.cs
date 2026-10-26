using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Pds.Desktop;

/// <summary>Replaceable hover feedback; text content stays opaque and no work runs at idle.</summary>
public static class CalmMotion
{
    public const int HoverEnterMs = 120, HoverLeaveMs = 180, TabMs = 140,
        ComposerMs = 180, ScreenMs = 200, DialogMs = 220;
    private sealed record Running(double Target, long Generation);
    private static readonly Dictionary<UIElement, Running> running = new();
    private static readonly ConditionalWeakTable<FrameworkElement, HoverConnection> hovers = new();
    private static long generation;
    internal static bool? AnimationOverride { get; set; }
    internal static bool AnimationsEnabled => AnimationOverride ?? SystemParameters.ClientAreaAnimation;
    internal static int ActiveCount => running.Count;
    public static readonly DependencyProperty HoverWashProperty = DependencyProperty.RegisterAttached(
        "HoverWash", typeof(bool), typeof(CalmMotion), new PropertyMetadata(false, HoverChanged));
    public static readonly DependencyProperty RevealOnVisibleProperty = DependencyProperty.RegisterAttached(
        "RevealOnVisible", typeof(bool), typeof(CalmMotion), new PropertyMetadata(false, RevealChanged));
    public static readonly DependencyProperty ShadeProperty = DependencyProperty.RegisterAttached(
        "Shade", typeof(bool), typeof(CalmMotion), new PropertyMetadata(false, ShadeChanged));
    public static readonly DependencyProperty SoftScrollAppearanceProperty=DependencyProperty.RegisterAttached(
        "SoftScrollAppearance",typeof(bool),typeof(CalmMotion),new PropertyMetadata(false,SoftScrollChanged));
    public static bool GetSoftScrollAppearance(DependencyObject value)=>(bool)value.GetValue(SoftScrollAppearanceProperty);
    public static void SetSoftScrollAppearance(DependencyObject value,bool enabled)=>value.SetValue(SoftScrollAppearanceProperty,enabled);
    private static void SoftScrollChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not FrameworkElement surface)return;
        surface.IsVisibleChanged-=ScrollVisibilityChanged;
        if((bool)args.NewValue){HookCleanup(surface);surface.IsVisibleChanged+=ScrollVisibilityChanged;}
    }
    private static void ScrollVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)
    {
        var surface=(FrameworkElement)sender;
        if((bool)args.NewValue)OpacityTo(surface,1,0,180);else Finish(surface);
    }
    private static readonly DependencyProperty CleanupHookedProperty = DependencyProperty.RegisterAttached(
        "CleanupHooked", typeof(bool), typeof(CalmMotion), new PropertyMetadata(false));
    public static bool GetHoverWash(DependencyObject value) => (bool)value.GetValue(HoverWashProperty);
    public static void SetHoverWash(DependencyObject value, bool enabled) => value.SetValue(HoverWashProperty, enabled);
    public static bool GetRevealOnVisible(DependencyObject value) => (bool)value.GetValue(RevealOnVisibleProperty);
    public static void SetRevealOnVisible(DependencyObject value, bool enabled) => value.SetValue(RevealOnVisibleProperty, enabled);
    public static bool GetShade(DependencyObject value) => (bool)value.GetValue(ShadeProperty);
    public static void SetShade(DependencyObject value, bool enabled) => value.SetValue(ShadeProperty, enabled);

    private static void HookCleanup(FrameworkElement element)
    {
        if ((bool)element.GetValue(CleanupHookedProperty)) return;
        element.SetValue(CleanupHookedProperty, true);
        element.Unloaded += (_, _) => Finish(element);
        element.IsVisibleChanged += (_, args) =>
        {
            if ((bool)args.NewValue) return;
            Finish(element);
            if (GetHoverWash(element)) element.SetCurrentValue(UIElement.OpacityProperty, 0d);
        };
    }
    private static void OpacityTo(UIElement element, double target, double from, int milliseconds)
    {
        var serial = ++generation;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.SetCurrentValue(UIElement.OpacityProperty, target);
        running.Remove(element);
        if (!AnimationsEnabled || milliseconds <= 0 || !element.IsVisible) return;
        if (element is FrameworkElement framework) HookCleanup(framework);
        running[element] = new(target, serial);
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (running.TryGetValue(element, out var current) && current.Generation == serial) Finish(element);
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
    public static void Reveal(FrameworkElement element, int milliseconds = ScreenMs)
    {
        // Fading a text subtree changes its rasterization while appearing. Keep text opaque.
        Finish(element);
        element.SetCurrentValue(UIElement.OpacityProperty, 1d);
    }
    private static void Finish(UIElement element)
    {
        if (!running.Remove(element, out var entry)) return;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.SetCurrentValue(UIElement.OpacityProperty, entry.Target);
    }
    internal static void Settle(DependencyObject root)
    {
        foreach (var element in running.Keys.ToArray())
            if (ReferenceEquals(element, root) || element is FrameworkElement framework && framework.IsDescendantOf(root)) Finish(element);
    }
    private static void HoverChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement overlay) return;
        overlay.Loaded -= ConnectHover; overlay.Unloaded -= DisconnectHover;
        if (!(bool)args.NewValue) { DisconnectHover(overlay, new RoutedEventArgs()); return; }
        overlay.Loaded += ConnectHover; overlay.Unloaded += DisconnectHover;
        if (overlay.IsLoaded) ConnectHover(overlay, new RoutedEventArgs());
    }
    private static void ConnectHover(object sender, RoutedEventArgs args)
    {
        var overlay = (FrameworkElement)sender;
        if (hovers.TryGetValue(overlay, out _)) return;
        var parent = overlay.TemplatedParent as ButtonBase;
        for (var node = VisualTreeHelper.GetParent(overlay); parent is null && node is not null; node = VisualTreeHelper.GetParent(node))
            parent = node as ButtonBase;
        if (parent is not null) hovers.Add(overlay, new HoverConnection(overlay, parent));
    }
    private static void DisconnectHover(object sender, RoutedEventArgs args)
    {
        var overlay = (FrameworkElement)sender;
        if (hovers.TryGetValue(overlay, out var connection)) { connection.Detach(); hovers.Remove(overlay); }
        Finish(overlay); overlay.SetCurrentValue(UIElement.OpacityProperty, 0d);
    }
    private sealed class HoverConnection
    {
        private readonly FrameworkElement overlay;
        private readonly ButtonBase host;
        public HoverConnection(FrameworkElement overlay, ButtonBase host)
        {
            this.overlay = overlay; this.host = host;
            host.MouseEnter += Enter; host.MouseLeave += Leave; host.IsEnabledChanged += EnabledChanged;
            if (host.IsMouseOver) Set(true);
        }
        private void Set(bool hovered)
        {
            var from = overlay.Opacity;
            OpacityTo(overlay, hovered && host.IsEnabled ? 0.045 : 0, from, hovered ? HoverEnterMs : HoverLeaveMs);
        }
        private void Enter(object sender, MouseEventArgs args) => Set(true);
        private void Leave(object sender, MouseEventArgs args) => Set(false);
        private void EnabledChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!host.IsEnabled) Set(false); }
        public void Detach()
        { host.MouseEnter -= Enter; host.MouseLeave -= Leave; host.IsEnabledChanged -= EnabledChanged; }
    }
    private static void RevealChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.IsVisibleChanged -= Revealed;
        if ((bool)args.NewValue) { HookCleanup(element); element.IsVisibleChanged += Revealed; }
        else Finish(element);
    }
    private static void Revealed(object sender, DependencyPropertyChangedEventArgs args)
    {
        if ((bool)args.NewValue) Reveal((FrameworkElement)sender, ComposerMs);
        else Finish((FrameworkElement)sender);
    }
    private static void ShadeChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.Loaded -= ShadeLoaded;
        if ((bool)args.NewValue) { element.Loaded += ShadeLoaded; ApplyShade(element); }
        else element.ClearValue(UIElement.EffectProperty);
    }
    private static void ShadeLoaded(object sender, RoutedEventArgs args) => ApplyShade((FrameworkElement)sender);
    private static void ApplyShade(FrameworkElement element)
    {
        if (SystemParameters.HighContrast || RenderCapability.Tier < 0x10000)
        { element.ClearValue(UIElement.EffectProperty); return; }
        var paper = (element.TryFindResource("Paper") as SolidColorBrush)?.Color ?? Colors.White;
        var dark = paper.R + paper.G + paper.B < 384;
        var effect = new DropShadowEffect { Color = Color.FromRgb(24, 20, 35), BlurRadius = 18,
            ShadowDepth = 2, Direction = 270, Opacity = dark ? 0.17 : 0.07, RenderingBias = RenderingBias.Performance };
        effect.Freeze(); element.Effect = effect;
    }
    internal static void RefreshShadows(DependencyObject root)
    {
        if (root is FrameworkElement element && GetShade(element)) ApplyShade(element);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) RefreshShadows(VisualTreeHelper.GetChild(root, i));
    }
}
