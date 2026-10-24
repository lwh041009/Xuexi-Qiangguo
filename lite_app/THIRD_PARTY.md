# Third-Party Components

The executable embeds unmodified Microsoft WebView2 SDK assemblies and the x64 loader:

- Microsoft.Web.WebView2.Core.dll, file version 1.0.3856.49.
- Microsoft.Web.WebView2.WinForms.dll, file version 1.0.3856.49.
- WebView2Loader.dll, from the same locally installed SDK bundle.

These Microsoft components remain subject to Microsoft's WebView2 SDK and redistributable license terms. Their inclusion does not transfer ownership. The Microsoft Edge WebView2 Runtime is installed separately on the system and is not included in this executable.

The application uses the Windows-provided .NET Framework and winsqlite3.dll; neither is bundled. Source and build scripts for the application are available in this directory. No Qt, Python, pywebview Python code, or Chromium distribution is bundled in StudyLite.exe.

The unmodified LICENSE.txt and NOTICE.txt from the official Microsoft.Web.WebView2 1.0.3856.49 NuGet package are included in licenses/. The package source is https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.3856.49/microsoft.web.webview2.1.0.3856.49.nupkg. Keep these notices with binary redistribution. This build is unsigned and is not an official application from the website operator or Microsoft.
