using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Writes manifest JSON that <see cref="ManifestParser"/> reads back unchanged. Defaults are left out, so
    /// written manifests stay as short as hand-written ones.
    /// </summary>
    internal static class ManifestWriter
    {
        public static string ToJson(int version, IEnumerable<ManifestModelDraft> models)
        {
            var root = new JObject
            {
                ["version"] = version,
                ["models"] = new JArray((models ?? Enumerable.Empty<ManifestModelDraft>())
                    .Where(model => model != null)
                    .Select(WriteModel))
            };
            return root.ToString(Formatting.Indented).Replace("\r\n", "\n");
        }

        private static JObject WriteModel(ManifestModelDraft model)
        {
            var element = new JObject { ["id"] = model.Id };
            if (!string.IsNullOrWhiteSpace(model.Revision))
            {
                element["revision"] = model.Revision;
            }

            if (model.Optional)
            {
                element["optional"] = true;
            }

            if (model.FileEntries?.Count > 0)
            {
                element["files"] = new JArray(model.FileEntries.Where(file => file != null).Select(WriteFile));
            }

            return element;
        }

        private static JObject WriteFile(ModelDependencyFileDefinition file)
        {
            var element = new JObject { ["path"] = file.Path };
            if (file.Optional)
            {
                element["optional"] = true;
            }

            if (!string.IsNullOrWhiteSpace(file.Label))
            {
                element["label"] = file.Label;
            }

            if (file.Conversion != null)
            {
                element["conversion"] = WriteConversion(file.Conversion);
            }

            return element;
        }

        private static JObject WriteConversion(OnnxConversionRecipe recipe)
        {
            var element = new JObject();
            var mode = OnnxConversionSettings.NormalizeQuantizationMode(recipe.QuantizationMode);
            var shapes = recipe.StaticInputShapeOverrides
                .Where(shape => shape != null && !string.IsNullOrWhiteSpace(shape.InputName))
                .ToArray();
            if (mode != "none" || shapes.Length == 0)
            {
                element["quantization"] = mode;
            }

            if (shapes.Length > 0)
            {
                var shapesElement = new JObject();
                foreach (var shape in shapes)
                {
                    shapesElement[shape.InputName] = new JArray(shape.Dimensions);
                }

                element["inputShapes"] = shapesElement;
            }

            return element;
        }
    }
}
