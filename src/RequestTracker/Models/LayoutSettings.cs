using Avalonia.Controls;

namespace RequestTracker.Models;

public class LayoutSettings
{
    public GridLengthDto LandscapeLeftColWidth { get; set; } = new(300, GridUnitType.Pixel);
    public GridLengthDto LandscapeHistoryRowHeight { get; set; } = new(150, GridUnitType.Pixel);

    public GridLengthDto PortraitTreeRowHeight { get; set; } = new(1, GridUnitType.Star);
    public GridLengthDto PortraitViewerRowHeight { get; set; } = new(1, GridUnitType.Star);
    public GridLengthDto PortraitHistoryRowHeight { get; set; } = new(150, GridUnitType.Pixel);

    /// <summary>JSON viewer: search index row height (row 2). Default 2*.</summary>
    public GridLengthDto JsonViewerSearchIndexRowHeight { get; set; } = new(2, GridUnitType.Star);
    /// <summary>JSON viewer: tree row height (row 4). Default *.</summary>
    public GridLengthDto JsonViewerTreeRowHeight { get; set; } = new(1, GridUnitType.Star);

    // Window State Persistence
    public double WindowWidth { get; set; } = 1000;
    public double WindowHeight { get; set; } = 800;
    public double WindowX { get; set; } = 100;
    public double WindowY { get; set; } = 100;
    public WindowState WindowState { get; set; } = WindowState.Normal;

    // Chat Window State
    public double ChatWindowWidth { get; set; } = 500;
    public double ChatWindowHeight { get; set; } = 550;
    public double ChatWindowX { get; set; }
    public double ChatWindowY { get; set; }

    /// <summary>Chat window: row height for template picker (row 1). Splitter below it. Default 1*.</summary>
    public GridLengthDto ChatTemplatePickerRowHeight { get; set; } = new(1, GridUnitType.Star);

    /// <summary>True if the chat window was open when the app was last closed; reopen it on next launch.</summary>
    public bool ChatWindowWasOpen { get; set; }
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
