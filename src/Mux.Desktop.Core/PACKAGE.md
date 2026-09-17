# Mux.Desktop.Core

The UI-framework-agnostic logic layer behind the [**mux**](https://github.com/jchristn/Mux) desktop app —
localization, view models, and services over [`Mux.Core`](https://www.nuget.org/packages/Mux.Core) and
[`Mux.Server`](https://www.nuget.org/packages/Mux.Server).

It has no Avalonia (or any UI toolkit) dependency, so the desktop's logic — thread/session management,
the embedded-server lifecycle, localization catalogs, and MVVM view models — is unit-testable in isolation
and reusable from any .NET UI shell. Distributed under the MIT License.
