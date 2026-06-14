using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Genie.App.Services;
using Genie.Core;
using Genie.Core.Events;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// Backs the dockable Scene panel — DR room/scene artwork. Subscribes to
/// <see cref="RoomImageEvent"/>, and (when <c>showimages</c> is on) fetches the
/// image via <see cref="RoomArtService"/> and shows it. Hidden by default; the
/// panel is empty for rooms without art (most of them), which is expected.
/// </summary>
public sealed class SceneViewModel : ReactiveObject
{
    private GenieCore?      _core;
    private RoomArtService? _art;

    // Last picture id we acted on — dedups DR's per-batch resends of the same id.
    private string _lastPictureId = "";

    /// <summary>The current room's artwork, or null when the room has none
    /// (or images are disabled). Bound to the panel's <c>Image.Source</c>.</summary>
    [Reactive] public Bitmap? Image { get; private set; }

    /// <summary>True when <see cref="Image"/> is showing — drives the panel's
    /// image-vs-placeholder visibility.</summary>
    [Reactive] public bool HasImage { get; private set; }

    public void Attach(GenieCore core)
    {
        _core = core;
        _art  = new RoomArtService(core.Config?.ArtDir ?? "Art");

        core.GameEvents
            .OfType<RoomImageEvent>()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e => OnRoomImage(e.PictureId));
    }

    // async void: the synchronous head (dedup + clear) runs on the UI thread
    // via ObserveOn; the fetch/decode is offloaded and marshalled back.
    private async void OnRoomImage(string pictureId)
    {
        if (pictureId == _lastPictureId) return;
        _lastPictureId = pictureId;

        // "0"/empty → the room has no art: clear whatever was showing.
        if (string.IsNullOrEmpty(pictureId) || pictureId == "0")
        {
            SetImage(null);
            return;
        }

        // showimages gate (read live so a #config change applies next room).
        if (_core?.Config?.ShowImages != true || _art is null) return;

        try
        {
            var path = await _art.GetImagePathAsync(pictureId).ConfigureAwait(false);
            if (path is null) return;

            // A late-arriving fetch for a room we've already left must not
            // overwrite the current one.
            if (pictureId != _lastPictureId) return;

            var bmp = await Task.Run(() => new Bitmap(path)).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (pictureId == _lastPictureId) SetImage(bmp);
                else bmp.Dispose();
            });
        }
        catch
        {
            // Art is best-effort — a failed fetch/decode just leaves no image.
        }
    }

    private void SetImage(Bitmap? bmp)
    {
        var old  = Image;
        Image    = bmp;
        HasImage = bmp is not null;
        old?.Dispose();
    }
}
