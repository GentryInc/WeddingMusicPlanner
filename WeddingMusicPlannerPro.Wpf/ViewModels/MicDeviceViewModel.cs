namespace WeddingMusicPlannerPro.Wpf.ViewModels;

/// <summary>A microphone (capture) device shown in the mic picker dropdown.</summary>
public sealed class MicDeviceViewModel
{
    public MicDeviceViewModel(string id, string friendlyName)
    {
        Id = id;
        FriendlyName = friendlyName;
    }

    public string Id { get; }
    public string FriendlyName { get; }
}
