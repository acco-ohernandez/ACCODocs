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
        // An exception escaping Execute crashes Revit, so this is the last line of defense
        // even though individual HandlerActions guard themselves. The action is cleared
        // BEFORE invoking so a throwing action can never stay armed and re-fire on the
        // next Raise. (Safety hardening over the production copy — carry it back at port.)
        public void Execute(UIApplication app)
        {
            Action<UIApplication> action = HandlerAction;
            HandlerAction = null;
            try
            {
                action?.Invoke(app);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModelessExternalEventHandler] HandlerAction threw: {ex}");
            }
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
