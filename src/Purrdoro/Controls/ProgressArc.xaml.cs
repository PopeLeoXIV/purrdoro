using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Purrdoro.Controls;

/// <summary>
/// Minimal circular progress indicator. Geometry is recomputed from the actual
/// rendered size, so it stays crisp at any size and display scale.
/// </summary>
public sealed partial class ProgressArc : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ProgressArc), new PropertyMetadata(0.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ProgressArc), new PropertyMetadata(6.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ProgressArc), new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(
        nameof(ArcBrush), typeof(Brush), typeof(ProgressArc), new PropertyMetadata(null, OnVisualPropertyChanged));

    public ProgressArc()
    {
        InitializeComponent();
        UpdateVisuals();
    }

    /// <summary>Progress from 0 to 1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? ArcBrush
    {
        get => (Brush?)GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ProgressArc)d).UpdateVisuals();

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => UpdateArcGeometry();

    private void UpdateVisuals()
    {
        var thickness = Math.Max(0.5, Thickness);
        Track.Stroke = TrackBrush;
        Track.StrokeThickness = thickness;
        Arc.Stroke = ArcBrush;
        Arc.StrokeThickness = thickness;
        UpdateArcGeometry();
    }

    private void UpdateArcGeometry()
    {
        var width = RootGrid.ActualWidth;
        var height = RootGrid.ActualHeight;
        var thickness = Math.Max(0.5, Thickness);
        var size = Math.Min(width, height);
        var value = double.IsNaN(Value) ? 0 : Math.Clamp(Value, 0, 1);

        if (size <= thickness || value <= 0.0005)
        {
            Arc.Data = null;
            return;
        }

        // Track the same centre line as the Ellipse, whose stroke sits inside its bounds.
        var radius = (size - thickness) / 2;
        var centre = new Point(width / 2, height / 2);

        if (value >= 0.9995)
        {
            Arc.Data = new EllipseGeometry { Center = centre, RadiusX = radius, RadiusY = radius };
            return;
        }

        var angle = value * 2 * Math.PI;
        var start = new Point(centre.X, centre.Y - radius);
        var end = new Point(centre.X + (radius * Math.Sin(angle)), centre.Y - (radius * Math.Cos(angle)));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = value > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        Arc.Data = geometry;
    }
}
