// The linked add-in sources (Shared\LinkLibraryConfig.cs) rely on these namespaces being
// globally available, as they are in the add-in project's Common\GlobalUsing.cs.
// (The WindowsDesktop SDK's implicit usings exclude System.IO to avoid the
// Shapes.Path name clash — the linked sources need it.)
global using System.Diagnostics;
global using System.IO;
global using System.Reflection;
