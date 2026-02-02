using Avalonia.Controls;

namespace RequestTracker.Models;

public class LayoutSettings
{
    public GridLengthDto LandscapeLeftColWidth { get; set; } = new(300, GridUnitType.Pixel);
    public GridLengthDto LandscapeHistoryRowHeight { get; set; } = new(150, GridUnitType.Pixel);

    public GridLengthDto PortraitTreeRowHeight { get; set; } = new(1, GridUnitType.Star);
    public GridLengthDto PortraitViewerRowHeight { get; set; } = new(1, GridUnitType.Star);
    public GridLengthDto PortraitHistoryRowHeight { get; set; } = new(150, GridUnitType.Pixel);

    // Window State Persistence
    public double WindowWidth { get; set; } = 1000;
    public double WindowHeight { get; set; } = 800;
    public double WindowX { get; set; } = 100;
    public double WindowY { get; set; } = 100;
    public WindowState WindowState { get; set; } = WindowState.Normal;
}

public class GridLengthDto
{
    public double Value { get; set; }
    public GridUnitType UnitType { get; set; }

    public GridLengthDto() { }

    public GridLengthDto(double value, GridUnitType unitType)
    {
        Value = value;
        UnitType = unitType;
    }

    public GridLength ToGridLength()
    {
        return new GridLength(Value, UnitType);
    }

    public static GridLengthDto FromGridLength(GridLength length)
    {
        return new GridLengthDto(length.Value, length.GridUnitType);
    }
}
