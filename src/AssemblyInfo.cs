// Orcl File Explorer: assembly identity and version.
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("Orcl File Explorer")]
[assembly: AssemblyProduct("Orcl File Explorer")]
[assembly: AssemblyDescription("Dual-pane file manager")]
// Version shown as major.minor.build with three-digit build (1.1.001). Bump the build number for every
// release; the minor number only changes when the owner says so.
[assembly: AssemblyVersion("1.1.12.0")]
[assembly: AssemblyFileVersion("1.1.12.0")]
// The test runner (tests\) reaches the app's internal classes directly.
[assembly: InternalsVisibleTo("orclfx.tests")]
