using ACCODocs.Logic.LinkLibrary;

namespace ACCODocs
{
    /// <summary>
    /// The ONE ribbon button for this add-in: toggles the Link Library dockable pane
    /// (DockablePane.Show()/Hide(), spec section 3). All other UI lives inside the pane.
    /// The pane itself is registered at startup by LinkLibraryPaneRegistrar — this
    /// command only resolves and toggles it.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ACCODocs : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;

            DockablePane pane = LinkLibraryPaneRegistrar.TryGetPane(uiapp);
            if (pane == null)
            {
                message = "The Link Library pane is not registered in this Revit session.";
                TaskDialog.Show(LinkLibraryPaneRegistrar.PaneTitle,
                    "The Link Library pane could not be found. It registers when Revit starts — " +
                    "if the add-in was just installed, restart Revit.");
                return Result.Failed;
            }

            if (pane.IsShown())
                pane.Hide();
            else
                pane.Show();

            return Result.Succeeded;
        }

        internal static PushButtonData GetButtonData()
        {
            // use this method to define the properties for this command in the Revit ribbon
            string buttonInternalName = "btnACCODocs";
            string buttonTitle = "ACCO Docs";

            Common.ButtonDataClass myButtonData = new Common.ButtonDataClass(
                buttonInternalName,
                buttonTitle,
                MethodBase.GetCurrentMethod().DeclaringType?.FullName,
                Properties.Resources.Blue_32,
                Properties.Resources.Blue_16,
                "Searchable library of ACCO documentation and reference links. Click to show or hide the pane.");

            return myButtonData.Data;
        }
    }

}
