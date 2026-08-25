namespace ACCODocs.Common
{
    // Copied from BTT_ACCORevit-Ribbons\RevitRibbon_MainSourceCode_Resources\Common\ModelessExternalEventHandler.cs
    // (namespace changed; the AlignEngine-specific Request* methods were left behind — they belong
    // to the Align Elements feature, not this reusable pattern). At port time this file is deleted
    // and callers rewire to the production copy. See LinkLibrary_DEV_Plan.md section 2.
    public class ModelessExternalEventHandler : IExternalEventHandler
    {
        public Action<UIApplication> HandlerAction { get; set; }
        private static UIDocument _uiDoc;

        // Execute is called by Revit on the next idle event after ExternalEvent.Raise().
        public void Execute(UIApplication app)
        {
            HandlerAction?.Invoke(app);
            HandlerAction = null;
        }

        public string GetName() => nameof(ModelessExternalEventHandler);

        public void SetUIDocument(UIDocument uiDoc)
        {
            _uiDoc = uiDoc;
        }

        public UIDocument GetUIDocument()
        {
            return _uiDoc;
        }
    }
}
