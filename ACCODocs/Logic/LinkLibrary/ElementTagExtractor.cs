using System.Collections.Generic;
using System.Linq;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// Extracts searchable tags from selected elements (spec section 8, step 4), in order:
    ///   1. BuiltInCategory enum name (NOT the localized display name)
    ///   2. Family name and type name
    ///   3. MEP system type / system classification, when present
    /// Runs inside the ExternalEvent handler — Revit API context only.
    /// </summary>
    public static class ElementTagExtractor
    {
        public static List<string> ExtractTags(Document doc, IEnumerable<Element> elements)
        {
            var tags = new List<string>();
            void Add(string tag)
            {
                if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag))
                    tags.Add(tag);
            }

            foreach (Element element in elements.Where(e => e != null))
            {
                // 1. BuiltInCategory enum name, e.g. "OST_PipeCurves" — matches the master
                //    tagVocabulary category facet and is locale-independent.
                Category category = element.Category;
                if (category != null)
                    Add(category.BuiltInCategory.ToString());

                // 2. Family and type names.
                if (doc.GetElement(element.GetTypeId()) is ElementType elementType)
                {
                    Add(elementType.FamilyName);
                    Add(elementType.Name);
                }

                // 3. MEP system classification / system name, when present.
                Parameter classification = element.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                if (classification != null)
                    Add(classification.AsValueString() ?? classification.AsString());

                if (element is MEPCurve mepCurve && mepCurve.MEPSystem != null)
                    Add(mepCurve.MEPSystem.Name);

                // 4. A dedicated tag-bearing shared parameter may be added later (spec section 8).
            }

            Debug.WriteLine($"[LinkLibrary] Extracted element tags: {string.Join(", ", tags)}");
            return tags;
        }
    }
}
