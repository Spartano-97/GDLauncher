public class ButtonConfig
{
    public string? Text { get; set; }
    public string? Type { get; set; } // "File" or "Website"
    public string? Path { get; set; }
    public string? StyleType { get; set; } // ToDo: "Primary", "Secondary", "Exit"
}

public class LauncherConfig
{
    public string? Title { get; set; }
    public string? SubTitle { get; set; }
    public List<ButtonConfig>? Buttons { get; set; }
}