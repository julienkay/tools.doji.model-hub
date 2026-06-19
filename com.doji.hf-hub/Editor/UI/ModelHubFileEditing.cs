using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Tracks per-repository file recipe edits while Model Hub window remains open.
    /// </summary>
    internal sealed class RepoFileEditSession
    {
        private readonly Dictionary<string, FileRecipeEditState> _filesByRepoPath;

        private RepoFileEditSession(
            string repository,
            string requestedRevision,
            IReadOnlyDictionary<string, FileRecipeEditState> filesByRepoPath)
        {
            Repository = repository;
            RequestedRevision = requestedRevision;
            _filesByRepoPath = new Dictionary<string, FileRecipeEditState>(filesByRepoPath, StringComparer.Ordinal);
        }

        public string Repository { get; }
        public string RequestedRevision { get; }
        public bool IsDirty => _filesByRepoPath.Values.Any(file => file.IsDirty);
        public bool HasValidationErrors => _filesByRepoPath.Values.Any(file => !string.IsNullOrWhiteSpace(file.ValidationMessage));

        /// <summary>
        /// Creates new session snapshot from current Model Hub item file state.
        /// </summary>
        public static RepoFileEditSession Create(ModelHubItem item)
        {
            var filesByRepoPath = (item?.Files ?? Array.Empty<ModelHubFileItem>())
                .Where(file => file.SupportsImportRecipeEditing)
                .ToDictionary(
                    file => file.RepoPath,
                    file => FileRecipeEditState.Create(file),
                    StringComparer.Ordinal);
            return new RepoFileEditSession(item?.Repository, item?.RequestedRevision, filesByRepoPath);
        }

        public bool Matches(ModelHubItem item)
        {
            if (!string.Equals(Repository, item?.Repository, StringComparison.Ordinal) ||
                !string.Equals(RequestedRevision, item?.RequestedRevision, StringComparison.Ordinal))
            {
                return false;
            }

            // Session invalidation is path-based because repo refresh may replace item instances while
            // still representing same editable file set.
            var itemPaths = (item?.Files ?? Array.Empty<ModelHubFileItem>())
                .Where(file => file.SupportsImportRecipeEditing)
                .Select(file => file.RepoPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var sessionPaths = _filesByRepoPath.Keys
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return itemPaths.SequenceEqual(sessionPaths, StringComparer.Ordinal);
        }

        public FileRecipeEditState GetOrCreateFileState(ModelHubFileItem file)
        {
            if (!_filesByRepoPath.TryGetValue(file.RepoPath, out var fileState))
            {
                fileState = FileRecipeEditState.Create(file);
                _filesByRepoPath[file.RepoPath] = fileState;
            }

            fileState.RefreshValidation();
            return fileState;
        }

        /// <summary>
        /// Builds detached recipes keyed by repo path for persistence through editor operations.
        /// </summary>
        public IReadOnlyDictionary<string, OnnxConversionRecipe> BuildRecipesByRepoPath()
        {
            var recipes = new Dictionary<string, OnnxConversionRecipe>(StringComparer.Ordinal);
            foreach (var pair in _filesByRepoPath)
            {
                recipes[pair.Key] = pair.Value.BuildRecipe();
            }

            return recipes;
        }
    }

    /// <summary>
    /// Holds mutable recipe state for one ONNX file row in Model Hub files tab.
    /// </summary>
    internal sealed class FileRecipeEditState
    {
        private readonly string _originalRecipeHash;
        private readonly OnnxInspectedModel _inspectedModel;

        private FileRecipeEditState(string repoPath, string originalRecipeHash, OnnxConversionRecipe recipe, OnnxInspectedModel inspectedModel)
        {
            RepoPath = repoPath;
            _originalRecipeHash = originalRecipeHash;
            _inspectedModel = inspectedModel;
            IsExpanded = true;
            QuantizationMode = ModelHubWindow.NormalizeQuantizationMode(recipe?.QuantizationMode);
            DynamicDimensions = BuildDynamicDimensions(recipe, inspectedModel);
            InputShapes = BuildInputShapes(inspectedModel);
            DynamicDimensionsExpanded = DynamicDimensions.Count > 0 || InputShapes.Count > 0;
            RefreshValidation();
        }

        public string RepoPath { get; }
        public bool IsExpanded { get; set; }
        public string QuantizationMode { get; set; }
        public List<DynamicDimensionEditState> DynamicDimensions { get; }
        public List<InputShapeDisplayState> InputShapes { get; }
        public bool DynamicDimensionsExpanded { get; set; }
        public string ValidationMessage { get; private set; }
        public bool IsDirty
        {
            get
            {
                if (!TryBuildRecipe(out var recipe))
                {
                    return true;
                }

                return !string.Equals(_originalRecipeHash, OnnxImportMetadataUtility.ComputeRecipeHash(recipe), StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Creates mutable file edit state from effective current recipe and optional ONNX inspection.
        /// </summary>
        public static FileRecipeEditState Create(ModelHubFileItem file)
        {
            var recipe = ModelHubWindow.CloneRecipe(file?.Recipe);
            var inspectedModel = OnnxModelInspector.Inspect(file?.RawCachedFilePath);
            return new FileRecipeEditState(
                file?.RepoPath,
                OnnxImportMetadataUtility.ComputeRecipeHash(recipe),
                recipe,
                inspectedModel);
        }

        /// <summary>
        /// Re-evaluates validation after UI edits mutate dynamic dimension values.
        /// </summary>
        public void RefreshValidation()
        {
            ValidationMessage = ValidateStaticDims();
        }

        /// <summary>
        /// Converts current editable state back into import recipe format expected by persistence layer.
        /// </summary>
        public OnnxConversionRecipe BuildRecipe()
        {
            return TryBuildRecipe(out var recipe)
                ? recipe
                : new OnnxConversionRecipe
                {
                    QuantizationMode = ModelHubWindow.NormalizeQuantizationMode(QuantizationMode),
                    StaticInputShapeOverrides = Array.Empty<OnnxStaticInputShapeOverride>()
                };
        }

        private bool TryBuildRecipe(out OnnxConversionRecipe recipe)
        {
            var staticOverrides = BuildStaticInputShapeOverrides();

            recipe = new OnnxConversionRecipe
            {
                QuantizationMode = ModelHubWindow.NormalizeQuantizationMode(QuantizationMode),
                StaticInputShapeOverrides = staticOverrides.ToArray()
            };
            return true;
        }

        private string ValidateStaticDims()
        {
            foreach (var dynamicDim in DynamicDimensions)
            {
                if (dynamicDim == null)
                {
                    continue;
                }

                if (dynamicDim.Value < -1)
                {
                    return "Dynamic dimensions must be -1 or non-negative.";
                }
            }

            return null;
        }

        /// <summary>
        /// Builds static overrides from inspected model defaults plus user changes to dynamic dimensions.
        /// </summary>
        private List<OnnxStaticInputShapeOverride> BuildStaticInputShapeOverrides()
        {
            var configuredInputs = new Dictionary<string, int[]>(StringComparer.Ordinal);
            foreach (var input in _inspectedModel?.Inputs ?? Array.Empty<OnnxInspectedInput>())
            {
                var dimensions = input.Dimensions
                    .Select(dimension => dimension.DefaultValue)
                    .ToArray();
                var changed = false;

                foreach (var dynamicDim in DynamicDimensions.Where(dimension =>
                             dimension.Bindings.Any(binding =>
                                 string.Equals(binding.InputName, input.Name, StringComparison.Ordinal))))
                {
                    var binding = dynamicDim.Bindings.FirstOrDefault(candidate =>
                        string.Equals(candidate.InputName, input.Name, StringComparison.Ordinal));
                    if (binding == null || binding.DimensionIndex < 0 || binding.DimensionIndex >= dimensions.Length)
                    {
                        continue;
                    }

                    dimensions[binding.DimensionIndex] = dynamicDim.Value;
                    changed |= dynamicDim.Value != -1;
                }

                if (changed)
                {
                    configuredInputs[input.Name] = dimensions;
                }
            }

            foreach (var existingOverride in BuildFallbackOverridesFromCurrentState())
            {
                configuredInputs[existingOverride.InputName] = existingOverride.Dimensions;
            }

            return configuredInputs
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new OnnxStaticInputShapeOverride
                {
                    InputName = pair.Key,
                    Dimensions = pair.Value
                })
                .ToList();
        }

        /// <summary>
        /// Falls back to raw UI bindings when ONNX inspection data is unavailable.
        /// </summary>
        private List<OnnxStaticInputShapeOverride> BuildFallbackOverridesFromCurrentState()
        {
            if (_inspectedModel != null)
            {
                return new List<OnnxStaticInputShapeOverride>();
            }

            return DynamicDimensions
                .SelectMany(dimension => dimension.Bindings.Select(binding => new { dimension.Value, binding.InputName, binding.DimensionIndex }))
                .GroupBy(dimension => dimension.InputName, StringComparer.Ordinal)
                .Select(group => new OnnxStaticInputShapeOverride
                {
                    InputName = group.Key,
                    Dimensions = group
                        .OrderBy(dimension => dimension.DimensionIndex)
                        .Select(dimension => dimension.Value)
                        .ToArray()
                })
                .Where(overrideEntry => overrideEntry.Dimensions.Any(value => value != -1))
                .ToList();
        }

        /// <summary>
        /// Collapses ONNX dynamic dimensions into shared editable rows keyed by semantic label.
        /// </summary>
        private static List<DynamicDimensionEditState> BuildDynamicDimensions(OnnxConversionRecipe recipe, OnnxInspectedModel inspectedModel)
        {
            var overrideLookup = (recipe?.StaticInputShapeOverrides ?? Array.Empty<OnnxStaticInputShapeOverride>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.InputName))
                .ToDictionary(item => item.InputName, item => item.Dimensions ?? Array.Empty<int>(), StringComparer.Ordinal);

            if (inspectedModel != null)
            {
                var dynamicDimensions = inspectedModel.Inputs
                    .SelectMany(input => input.Dimensions
                        .Where(dimension => dimension.IsDynamicByDefault)
                        .Select(dimension => new InspectedDynamicDimensionRef
                        {
                            InputName = input.Name,
                            Dimension = dimension
                        }))
                    .ToArray();

                return dynamicDimensions
                    .GroupBy(item => item.Dimension.IsNamed
                        ? item.Dimension.Label
                        : $"{item.InputName}[{item.Dimension.Index}]",
                        StringComparer.Ordinal)
                    .Select(group => new DynamicDimensionEditState
                    {
                        Label = group.Key,
                        Value = GetSharedOverrideValue(group, overrideLookup),
                        Bindings = group
                            .Select(item => new DynamicDimensionBinding
                            {
                                InputName = item.InputName,
                                DimensionIndex = item.Dimension.Index
                            })
                            .ToList()
                    })
                    .ToList();
            }

            return overrideLookup
                .SelectMany(pair => pair.Value.Select((value, index) => new DynamicDimensionEditState
                {
                    Label = $"{pair.Key}[{index}]",
                    Value = value,
                    Bindings = new List<DynamicDimensionBinding>
                    {
                        new()
                        {
                            InputName = pair.Key,
                            DimensionIndex = index
                        }
                    }
                }))
                .ToList();
        }

        /// <summary>
        /// Builds read-only shape rows used when inspected inputs have no editable dynamic dimensions.
        /// </summary>
        internal static List<InputShapeDisplayState> BuildInputShapes(OnnxInspectedModel inspectedModel)
        {
            return (inspectedModel?.Inputs ?? Array.Empty<OnnxInspectedInput>())
                .Select(input => new InputShapeDisplayState(
                    input.Name,
                    input.Dimensions
                        .Select(dimension => new InputShapeDimensionDisplayState(
                            dimension.Label,
                            dimension.Index,
                            FormatInputShapeDimension(dimension),
                            dimension.IsDynamicByDefault))
                        .ToArray()))
                .ToList();
        }

        private static string FormatInputShapeDimension(OnnxInspectedDimension dimension)
        {
            if (!dimension.IsDynamicByDefault)
            {
                return dimension.DefaultValue.ToString();
            }

            return dimension.IsNamed
                ? dimension.Label
                : "-1";
        }

        /// <summary>
        /// Reuses first explicit override value among all dimensions that share one editable label.
        /// </summary>
        private static int GetSharedOverrideValue(
            IEnumerable<InspectedDynamicDimensionRef> groupedDimensions,
            IReadOnlyDictionary<string, int[]> overrideLookup)
        {
            foreach (var item in groupedDimensions)
            {
                var value = TryGetOverrideValue(overrideLookup, item.InputName, item.Dimension.Index);
                if (value != -1)
                {
                    return value;
                }
            }

            return -1;
        }

        private static int TryGetOverrideValue(IReadOnlyDictionary<string, int[]> overrideLookup, string inputName, int dimensionIndex)
        {
            return overrideLookup.TryGetValue(inputName, out var dimensions) &&
                dimensions != null &&
                dimensionIndex >= 0 &&
                dimensionIndex < dimensions.Length
                    ? dimensions[dimensionIndex]
                    : -1;
        }
    }

    /// <summary>
    /// Editable row model for one grouped dynamic dimension entry in files tab UI.
    /// </summary>
    internal sealed class DynamicDimensionEditState
    {
        public string Label { get; set; }
        public int Value { get; set; }
        public List<DynamicDimensionBinding> Bindings { get; set; } = new();
    }

    /// <summary>
    /// Read-only display model for one inspected ONNX input shape.
    /// </summary>
    internal sealed class InputShapeDisplayState
    {
        private readonly InputShapeDimensionDisplayState[] _dimensions;

        public InputShapeDisplayState(string name, IReadOnlyList<InputShapeDimensionDisplayState> dimensions)
        {
            Name = name;
            _dimensions = dimensions?.ToArray() ?? Array.Empty<InputShapeDimensionDisplayState>();
        }

        public string Name { get; }
        public IReadOnlyList<InputShapeDimensionDisplayState> Dimensions => _dimensions;
        public string DisplayValue => $"[{string.Join(", ", _dimensions.Select(dimension => dimension.DisplayValue))}]";
    }

    /// <summary>
    /// Read-only display model for one inspected ONNX input dimension.
    /// </summary>
    internal sealed class InputShapeDimensionDisplayState
    {
        public InputShapeDimensionDisplayState(string label, int index, string displayValue, bool isDynamic)
        {
            Label = label;
            Index = index;
            DisplayValue = displayValue;
            IsDynamic = isDynamic;
        }

        public string Label { get; }
        public int Index { get; }
        public string DisplayValue { get; }
        public bool IsDynamic { get; }
    }

    /// <summary>
    /// Maps one editable grouped dimension row back to concrete ONNX input and dimension index.
    /// </summary>
    internal sealed class DynamicDimensionBinding
    {
        public string InputName { get; set; }
        public int DimensionIndex { get; set; }
    }

    /// <summary>
    /// Temporary grouping helper that carries inspected ONNX dimension identity through LINQ transforms.
    /// </summary>
    internal sealed class InspectedDynamicDimensionRef
    {
        public string InputName { get; set; }
        public OnnxInspectedDimension Dimension { get; set; }
    }
}
