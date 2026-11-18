// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UnknownsSmokescreen;

internal static class RoundOutputs
{
    private static bool roundOpen, ending;

    internal static void Begin()
    {
        if (roundOpen) return;
        roundOpen = true;
        ending = false;
        ViewerServer.Clear();
        DelayedRecording.Reset();
    }

    internal static void Tick(bool started)
    {
        ViewerServer.Configure(SettingsMenu.PatchedAtStartup && SmokescreenPlugin.ViewerPage.Value ? SmokescreenPlugin.ViewerPort.Value : 0);
        if (roundOpen && !started && !ending) Abort();
    }

    internal static void MarkEnding() => ending = roundOpen;

    internal static void Abort()
    {
        if (!roundOpen) return;
        ending = roundOpen = false;
        DelayedRecording.Reset();
        OverlayContent.ReleaseReplay();
    }

    // TOR befüllt die Liste in OnGameEnd und leert sie in SetEverythingUp.
    internal static void Finish()
    {
        if (!roundOpen) return;
        ending = roundOpen = false;
        DelayedRecording.Finish();
        OverlayContent.ReleaseReplay();
        if (!SmokescreenPlugin.ViewerPage.Value) return;
        try
        {
            var assembly = typeof(TheOtherRoles.RoleInfo).Assembly;
            var data = assembly.GetType("TheOtherRoles.Patches.AdditionalTempData");
            if (data?.GetField("playerRoles")?.GetValue(null) is not IEnumerable rows) return;
            var result = new List<ViewerServer.Player>();
            foreach (var row in rows)
            {
                var type = row.GetType();
                if (type.GetProperty("PlayerName")?.GetValue(row) is not string name
                    || type.GetProperty("RoleNames")?.GetValue(row) is not string roles) continue;
                roles = Regex.Replace(roles, @"</?(?:color|size)(?:=[^<>]*)?>", "", RegexOptions.IgnoreCase);
                result.Add(new ViewerServer.Player(name, roles));
            }
            if (result.Count > 0) ViewerServer.Publish(result);
        }
        catch (Exception e) { SmokescreenPlugin.Logger.LogWarning("Zuschauerseite: TOR-Auswertung nicht verfügbar (" + e.GetType().Name + ")"); }
    }
}

internal static class ViewerServer
{
    internal const int DefaultPort = 18765;
    internal sealed record Player(string Name, string Roles);
    private static byte[] result;
    private static CancellationTokenSource cancellation;
    private static TcpListener listener;
    private static int requested;
    private static volatile bool running;
    internal static bool Running => running;
    internal static bool Failed => requested != 0 && !running;
    internal static string Address => "http://127.0.0.1:" + SafePort(SmokescreenPlugin.ViewerPort.Value) + "/";
    internal static int SafePort(int value) => value == 8765 ? DefaultPort : value;

    internal static void Clear() => Volatile.Write(ref result, null);
    internal static void Publish(List<Player> players) => Volatile.Write(ref result, JsonSerializer.SerializeToUtf8Bytes(players));

    internal static void Configure(int port)
    {
        port = SafePort(port);
        if (port == requested) return;
        CloseListener();
        requested = port;
        if (port == 0) { Clear(); return; }
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start(4);
            cancellation = new CancellationTokenSource();
            running = true;
            _ = Serve(listener, port, cancellation.Token);
        }
        catch (SocketException e)
        {
            CloseListener();
            SmokescreenPlugin.Logger.LogWarning("Zuschauerseite: lokaler Port nicht verfügbar (" + e.SocketErrorCode + ")");
        }
    }

    private static void CloseListener()
    {
        cancellation?.Cancel();
        listener?.Stop();
        cancellation?.Dispose();
        listener = null;
        cancellation = null;
        running = false;
    }

    internal static void Stop()
    {
        CloseListener();
        requested = 0;
        Clear();
    }

    private static async Task Serve(TcpListener server, int port, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await server.AcceptTcpClientAsync(stop).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                deadline.CancelAfter(1200);
                try { await Reply(client.GetStream(), port, deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (SocketException) { }
            }
        }
        catch (Exception) when (stop.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (ReferenceEquals(listener, server)) running = false;
            SmokescreenPlugin.Logger.LogWarning("Zuschauerseite: Serverfehler (" + e.GetType().Name + ")");
            server.Stop();
        }
    }

    private static async Task Reply(NetworkStream stream, int port, CancellationToken stop)
    {
        var buffer = new byte[4096];
        int length = 0;
        string headers = null;
        while (length < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(length), stop).ConfigureAwait(false);
            if (count == 0) return;
            length += count;
            string text = Encoding.ASCII.GetString(buffer, 0, length);
            int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) continue;
            headers = text[..end];
            break;
        }
        if (headers == null) return;
        var lines = headers.Split("\r\n");
        int hosts = 0;
        foreach (var line in lines)
        {
            if (!line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) continue;
            if (line[5..].Trim() != "127.0.0.1:" + port) return;
            hosts++;
        }
        if (hosts != 1) return;
        var request = lines[0].Split(' ');
        if (request.Length != 3 || request[0] != "GET") return;
        var (status, type, body) = request[1] switch
        {
            "/" => ("200 OK", "text/html; charset=utf-8", Page),
            "/roles" => ("200 OK", "application/json", Volatile.Read(ref result) ?? Empty),
            _ => ("404 Not Found", "text/plain", Array.Empty<byte>())
        };
        var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n"
            + "Connection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n"
            + "Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'\r\n\r\n");
        await stream.WriteAsync(response, stop).ConfigureAwait(false);
        await stream.WriteAsync(body, stop).ConfigureAwait(false);
    }

    private static readonly byte[] Empty = Encoding.UTF8.GetBytes("[]");
    private static readonly byte[] Page = Encoding.UTF8.GetBytes("""
        <!doctype html>
        <html lang="de"><meta charset="utf-8"><title>Smokescreen | Rollenübersicht</title>
        <style>
        body { margin:0; color:#f2f4f8; font:22px system-ui,sans-serif }
        main { display:inline-block; background:#16202a; padding:24px 36px; border-radius:14px }
        h1 { font-size:28px; margin:0 0 12px } ul { margin:0; padding:0; list-style:none }
        li { padding:5px 0 } strong { color:#9ee7ce }
        </style>
        <main hidden><h1>Rollen am Rundenende</h1><ul></ul></main>
        <script>
        const panel = document.querySelector('main'), list = document.querySelector('ul');
        async function update() {
            let rows = [];
            try {
                const response = await fetch('/roles', {cache:'no-store', signal:AbortSignal.timeout(1000)});
                if (response.ok) rows = await response.json();
            } catch {}
            list.replaceChildren();
            for (const row of rows) {
                const line = document.createElement('li'), name = document.createElement('strong');
                name.textContent = row.Name;
                line.append(name, ' · ' + row.Roles);
                list.append(line);
            }
            panel.hidden = rows.length === 0;
            setTimeout(update, 350);
        }
        update();
        </script></html>
        """);
}
