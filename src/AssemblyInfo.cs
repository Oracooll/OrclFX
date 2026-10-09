// OrclFX: assembly identity and version.
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("OrclFX")]
// The product name stays "Orcl File Explorer": versions up to 1.1.035 only accept an update whose product name is
// that (Updater.Download). The description (AssemblyTitle, shown by Task Manager) is the app's name, OrclFX.
[assembly: AssemblyProduct("Orcl File Explorer")]
[assembly: AssemblyDescription("Dual-pane file manager")]
// Version shown as major.minor.build with three-digit build (1.1.001). Bump the build number for every
// release; the minor number only changes when the owner says so.
[assembly: AssemblyVersion("1.1.42.0")]
[assembly: AssemblyFileVersion("1.1.42.0")]
// The test runner (tests\) reaches the app's internal classes directly.
[assembly: InternalsVisibleTo("orclfx.tests")]
