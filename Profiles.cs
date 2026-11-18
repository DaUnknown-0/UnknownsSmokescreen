// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
namespace UnknownsSmokescreen;

internal enum Profile { Custom, Live, Recording, Private }

internal static class Profiles
{
    internal static Profile Current => SmokescreenPlugin.ActiveProfile.Value;
    internal static bool AllowsFeed => Current is Profile.Custom or Profile.Recording;
    internal static bool AllowsViewerOutputs => Current is Profile.Custom or Profile.Live;
    internal static bool AllowsReplay => Current != Profile.Private;

    internal static void Cycle() => SmokescreenPlugin.ActiveProfile.Value = Current switch
    {
        Profile.Custom => Profile.Live,
        Profile.Live => Profile.Recording,
        Profile.Recording => Profile.Private,
        _ => Profile.Custom
    };

    internal static void Changed()
    {
        if (SmokescreenPlugin.FeedUnlocked) SmokescreenPlugin.SetFeedUnlocked(false);
        if (!AllowsViewerOutputs)
        {
            ViewerServer.Stop();
            ViewerExplanation.Clear();
        }
        if (!AllowsReplay)
        {
            DelayedRecording.Reset();
            OverlayContent.ReleaseReplay();
        }
    }
}
