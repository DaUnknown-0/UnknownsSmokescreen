// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace UnknownsSmokescreen;

internal static class DelayedRecording
{
    internal const int MaxWidth = 480, MaxHeight = 270;
    private const int Capacity = 90, DurationMs = 30000, IntervalMs = 334;
    private static Frame[] ring;
    private static int next, count;
    private static long due;
    private static Frame[] finished;
    private static Task export;
    private static volatile string status;
    private static string exportedId;
    internal static string Status => status switch
    {
        null => null,
        "capture.replay.saved" => Texts.F("capture.replay.saved", exportedId),
        var key => Texts.T(key)
    };
    internal static bool CanExport => finished?.Length > 0 && (export == null || export.IsCompleted);

    private sealed class Frame
    {
        internal byte[] Rgba;
        internal int Width, Height;
        internal long At;
    }

    internal static bool TryBeginSample()
    {
        long now = Environment.TickCount64;
        if (now < due) return false;
        due = now + IntervalMs;
        return true;
    }

    internal static void Push(IntPtr rgba, int width, int height)
    {
        ring ??= new Frame[Capacity];
        var frame = ring[next] ??= new Frame();
        int bytes = width * height * 4;
        if (frame.Rgba?.Length != bytes) frame.Rgba = new byte[bytes];
        Marshal.Copy(rgba, frame.Rgba, 0, bytes);
        frame.Width = width;
        frame.Height = height;
        frame.At = Environment.TickCount64;
        next = (next + 1) % Capacity;
        count = Math.Min(count + 1, Capacity);
    }

    internal static void Finish()
    {
        if (count == 0) return;
        long cutoff = Environment.TickCount64 - DurationMs;
        finished = Enumerable.Range(0, count).Select(i => ring[(next - count + i + Capacity) % Capacity])
            .Where(frame => frame.At >= cutoff).ToArray();
        ring = null;
        count = next = 0;
        status = finished.Length > 0 ? "capture.replay.ready" : null;
    }

    internal static void Reset()
    {
        ring = null;
        finished = null;
        count = next = 0;
        due = 0;
        if (export == null || export.IsCompleted) status = null;
    }

    internal static void Export()
    {
        if (!CanExport || !SmokescreenPlugin.DelayedReplay.Value) return;
        var frames = finished;
        string root = Path.Combine(BepInEx.Paths.BepInExRootPath, "SmokescreenReplays");
        status = "capture.replay.saving";
        export = Task.Run(() =>
        {
            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
            string partial = Path.Combine(root, id + ".partial");
            try
            {
                Directory.CreateDirectory(partial);
                for (int i = 0; i < frames.Length; i++)
                    WriteBitmap(Path.Combine(partial, i.ToString(CultureInfo.InvariantCulture) + ".bmp"), frames[i]);
                var times = frames.Select(f => f.At - frames[0].At);
                File.WriteAllText(Path.Combine(partial, "Wiederholung.html"), Player.Replace("__TIMES__", JsonSerializer.Serialize(times)));
                Directory.Move(partial, Path.Combine(root, id));
                exportedId = id;
                status = "capture.replay.saved";
            }
            catch (Exception e)
            {
                status = "capture.replay.failed";
                SmokescreenPlugin.Logger.LogWarning("Bildexport: " + e.GetType().Name);
                try
                {
                    foreach (string file in Directory.EnumerateFiles(partial)) File.Delete(file);
                    Directory.Delete(partial);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        });
    }

    private static void WriteBitmap(string path, Frame frame)
    {
        using var file = new BinaryWriter(File.Create(path));
        int stride = (frame.Width * 3 + 3) & ~3;
        int imageSize = stride * frame.Height;
        file.Write((ushort)0x4D42);
        file.Write(54 + imageSize);
        file.Write(0);
        file.Write(54);
        file.Write(40);
        file.Write(frame.Width);
        file.Write(frame.Height);
        file.Write((ushort)1);
        file.Write((ushort)24);
        for (int i = 0; i < 6; i++) file.Write(i == 1 ? imageSize : 0);
        var row = new byte[stride];
        for (int y = 0; y < frame.Height; y++)
        {
            int source = y * frame.Width * 4;
            for (int x = 0; x < frame.Width; x++)
            {
                row[x * 3] = frame.Rgba[source + 2];
                row[x * 3 + 1] = frame.Rgba[source + 1];
                row[x * 3 + 2] = frame.Rgba[source];
                source += 4;
            }
            file.Write(row);
        }
    }

    private const string Player = """
        <!doctype html>
        <html lang="de"><meta charset="utf-8"><title>Smokescreen | Rundenwiederholung</title>
        <style>
        body { margin:2rem; background:#131a22; color:white; font:16px system-ui,sans-serif }
        img { display:block; width:min(100%,960px); margin-bottom:1rem }
        input { width:min(65vw,700px) }
        </style>
        <img src="0.bmp" alt="Originalsicht der Rundenaufnahme">
        <button type="button">Abspielen</button>
        <input type="range" min="0" value="0" aria-label="Bild wählen">
        <script>
        const times = __TIMES__, image = document.querySelector('img');
        const button = document.querySelector('button'), slider = document.querySelector('input');
        let timer;
        slider.max = times.length - 1;
        function show(i) { slider.value = i; image.src = i + '.bmp'; }
        function pause() { clearTimeout(timer); timer = undefined; button.textContent = 'Abspielen'; }
        function play(i) {
            show(i);
            if (i === times.length - 1) { pause(); return; }
            timer = setTimeout(() => play(i + 1), times[i + 1] - times[i]);
        }
        slider.oninput = () => { pause(); show(+slider.value); };
        button.onclick = () => {
            if (timer !== undefined) { pause(); return; }
            button.textContent = 'Pause';
            play(+slider.value === times.length - 1 ? 0 : +slider.value);
        };
        </script></html>
        """;
}
