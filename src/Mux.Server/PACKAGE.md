# Mux.Server

The optional local REST + WebSocket server behind [**mux**](https://github.com/jchristn/Mux) — a
backend-agnostic AI coding agent — packaged so you can host the mux engine over loopback and drive it from
any client.

`Mux.Server` exposes [`Mux.Core`](https://www.nuget.org/packages/Mux.Core) over an HTTP API (built on
[Watson](https://www.nuget.org/packages/Watson)): configured endpoints, streaming agentic chat over
Server-Sent Events, persisted sessions (list, detail, upsert, export, delete, and label/tag metadata),
usage telemetry with label/tag filtering, and a single-file web dashboard. It binds loopback by default and
supports optional bearer-token auth.

## Quick start

```csharp
using Mux.Core.Sessions;
using Mux.Server;

SessionStore sessions = new SessionStore(); // ~/.mux/sessions
using MuxServer server = new MuxServer(
    new Mux.Core.Settings.RestServerSettings { Hostname = "127.0.0.1", Port = 8710 },
    productVersion: "1.0.0",
    sessionStore: sessions,
    endpointsProvider: () => Mux.Core.Settings.SettingsLoader.LoadEndpoints());
server.Start();
// GET http://127.0.0.1:8710/dashboard
```

See the [REST API reference](https://github.com/jchristn/Mux/blob/main/docs/REST_API.md) for the full
surface. Distributed under the MIT License.
