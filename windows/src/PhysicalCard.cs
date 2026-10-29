using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snapline
{
    internal sealed class CardSpring
    {
        internal Vector Position { get; private set; }
        internal Vector Velocity { get; private set; }
        private Vector target;
        internal bool Aim(Vector value)
        {
            target = new Vector(Math.Max(-6, Math.Min(6, value.X)), Math.Max(-3.5, Math.Min(1, value.Y)));
            return Position != target || Velocity.LengthSquared > 0;
        }
        internal void Kick(double amount)
        { Velocity = new Vector(Math.Max(-90, Math.Min(90, Velocity.X + Math.Max(-45, Math.Min(45, amount * 4)))), Velocity.Y); }
        internal bool Step(double elapsed)
        {
            double remaining = Math.Max(0, Math.Min(.05, elapsed));
            while (remaining > 0) {
                double dt = Math.Min(1.0 / 120, remaining);
                // Same unit-mass spring as the clothing line: stiffness 100, damping 10.
                Velocity += ((target - Position) * 100 - Velocity * 10) * dt;
                Position += Velocity * dt;
                remaining -= dt;
            }
            if ((Position - target).Length > .01 || Velocity.Length > .05) return true;
            Position = target; Velocity = new Vector(); return false;
        }
        internal void Reset() { Position = Velocity = target = new Vector(); }
    }

    // Gesture-only native feedback; no layout changes, global input hooks or idle frame loop.
    internal sealed class PhysicalCard
    {
        private static readonly DependencyProperty MotionProperty = DependencyProperty.RegisterAttached(
            "Motion", typeof(PhysicalCard), typeof(PhysicalCard));
        private readonly FrameworkElement element;
        private readonly RotateTransform rotation;
        private readonly ScaleTransform scale;
        private readonly CardSpring spring = new CardSpring();
        private readonly Stopwatch clock = new Stopwatch();
        private TimeSpan previous;
        private double pointerAngle, force;
        private bool hovering, pressed;
        internal bool Running { get; private set; }
        internal static PhysicalCard Get(FrameworkElement element) { return (PhysicalCard)element.GetValue(MotionProperty); }
        internal static PhysicalCard Attach(FrameworkElement element)
        {
            var rotation = new RotateTransform(); var scale = new ScaleTransform(1, 1);
            var transforms = new TransformGroup(); transforms.Children.Add(element.RenderTransform);
            transforms.Children.Add(rotation); transforms.Children.Add(scale);
            element.RenderTransform = transforms; element.RenderTransformOrigin = new Point(.5, .5);
            return new PhysicalCard(element, rotation, scale);
        }
        internal PhysicalCard(FrameworkElement element, RotateTransform rotation, ScaleTransform scale)
        {
            this.element = element; this.rotation = rotation; this.scale = scale;
            element.SetValue(MotionProperty, this);
            element.MouseEnter += Move;
            element.MouseMove += Move;
            element.MouseLeave += delegate { Release(); };
            element.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(delegate(object sender, MouseButtonEventArgs e) {
                if (e.StylusDevice != null || e.ChangedButton != MouseButton.Left || IsButton(e.OriginalSource as DependencyObject)) return;
                PointAt(e.GetPosition(element), true);
            }), true);
            element.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(delegate(object sender, MouseButtonEventArgs e) {
                if (e.ChangedButton == MouseButton.Left) { pressed = false; Aim(); }
            }), true);
            element.LostMouseCapture += delegate { pressed = false; Aim(); };
            element.PreviewKeyDown += delegate { Reset(); };
            element.Unloaded += delegate { Reset(); };
            element.IsVisibleChanged += delegate { if (!element.IsVisible) Reset(); };
            element.IsEnabledChanged += delegate { if (!element.IsEnabled) Reset(); };
        }
        private bool IsButton(DependencyObject source)
        {
            while (source != null && source != element) {
                if (source is System.Windows.Controls.Primitives.ButtonBase) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }
        private void Move(object sender, MouseEventArgs e)
        {
            if (e.StylusDevice != null || IsButton(e.OriginalSource as DependencyObject)) return;
            PointAt(e.GetPosition(element), pressed);
        }
        internal void PointAt(Point point, bool down)
        {
            hovering = true; pressed = down;
            pointerAngle = Math.Max(-1, Math.Min(1, (point.X / Math.Max(1, element.ActualWidth) - .5) * 2)) * 2;
            Aim();
        }
        internal void Pull(double angle) { force = angle; Aim(); }
        internal void Release() { hovering = pressed = false; pointerAngle = 0; Aim(); }
        internal void Kick(double amount)
        { if (!Allowed) { Reset(); return; } spring.Kick(amount); Start(); }
        private bool Allowed { get { return element.IsLoaded && element.IsVisible && element.IsEnabled && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast; } }
        private void Aim()
        {
            if (!Allowed) { Reset(); return; }
            if (spring.Aim(new Vector(pointerAngle + force, scale == null ? 0 : pressed ? -3.5 : hovering ? 1 : 0))) Start();
            else Stop();
        }
        private void Start()
        {
            if (Running) return;
            Running = true; previous = TimeSpan.Zero; clock.Restart(); CompositionTarget.Rendering += Frame;
        }
        private void Frame(object sender, EventArgs e)
        {
            if (!Allowed) { Reset(); return; }
            TimeSpan now = clock.Elapsed; bool moving = spring.Step((now - previous).TotalSeconds); previous = now;
            rotation.Angle = spring.Position.X;
            if (scale != null) scale.ScaleX = scale.ScaleY = 1 + spring.Position.Y / 100;
            if (!moving) Stop();
        }
        private void Stop()
        { if (Running) CompositionTarget.Rendering -= Frame; Running = false; clock.Stop(); }
        internal void Reset()
        {
            Stop(); spring.Reset(); hovering = pressed = false; pointerAngle = force = 0;
            rotation.Angle = 0; if (scale != null) scale.ScaleX = scale.ScaleY = 1;
        }
    }
}
