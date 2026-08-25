using ACCODocs.Logic.LinkLibrary;

namespace ACCODocs
{
    internal class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            // Register the Link Library dockable pane. Must happen during OnStartup (spec section 3).
            LinkLibraryPaneRegistrar.Register(app);

            // 1. Create ribbon tab
            string tabName = "ORH Dev";
            try
            {
                app.CreateRibbonTab(tabName);
            }
            catch (Exception)
            {
                Debug.Print("Tab already exists.");
            }

            // 2. Create ribbon panel 
            RibbonPanel panel = Common.Utils.CreateRibbonPanel(app, tabName, "In Development");

            // 3. Create button data instances
            // Single button by design: "ACCO Docs" toggles the Link Library pane;
            // all other UI lives inside the pane.
            PushButtonData btnData1 = Cmd_ACCODocs.GetButtonData();

            //// 4. Create buttons
            PushButton myButton1 = panel.AddItem(btnData1) as PushButton;

            // NOTE:
            // To create a new tool, copy lines 35 and 39 and rename the variables to "btnData3" and "myButton3". 
            // Change the name of the tool in the arguments of line 

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }
    }

}
