using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Stores project-scoped editor configuration: build delivery, optional-file selections, and
    /// conversion overrides. Installed package manifests stay unchanged; the project's choices live here.
    /// </summary>
    [FilePath("ProjectSettings/Doji.AI.ModelHubSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ProjectSettings : ScriptableSingleton<ProjectSettings>
    {
        [SerializeField]
        private bool _useDefaultCacheLocation = true;

        [SerializeField]
        private string _customCacheLocation = string.Empty;

        [SerializeField]
        private ModelDelivery _defaultDelivery = ModelDelivery.Bundle;

        [SerializeField]
        private List<DeliveryOverride> _deliveryOverrides = new();

        [SerializeField]
        private List<OptionalFileSelection> _optionalFileSelections = new();

        [SerializeField]
        private List<ConversionOverride> _conversionOverrides = new();

        /// <summary>
        /// Returns the optional files this project turned on for one manifest owner's model, sorted. Empty when the
        /// project turned none on.
        /// </summary>
        public string[] GetEnabledOptionalFiles(string ownerId, string modelId)
        {
            var entry = FindSelection(ownerId, modelId);
            return entry?.enabledFiles?.ToArray() ?? Array.Empty<string>();
        }

        /// <summary>
        /// Stores the optional files this project turns on for one manifest owner's model. An empty list removes the
        /// entry.
        /// </summary>
        public void SetEnabledOptionalFiles(string ownerId, string modelId, IEnumerable<string> enabledFiles)
        {
            var files = (enabledFiles ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            var entry = FindSelection(ownerId, modelId);
            if (files.Count == 0)
            {
                if (entry != null)
                {
                    _optionalFileSelections.Remove(entry);
                    Save(true);
                }

                return;
            }

            if (entry != null && entry.enabledFiles != null && entry.enabledFiles.SequenceEqual(files, StringComparer.Ordinal))
            {
                return;
            }

            if (entry == null)
            {
                _optionalFileSelections ??= new List<OptionalFileSelection>();
                _optionalFileSelections.Add(new OptionalFileSelection { ownerId = ownerId, modelId = modelId });
                _optionalFileSelections.Sort((left, right) =>
                {
                    var byOwner = string.CompareOrdinal(left.ownerId, right.ownerId);
                    return byOwner != 0 ? byOwner : string.CompareOrdinal(left.modelId, right.modelId);
                });
                entry = FindSelection(ownerId, modelId);
            }

            entry.enabledFiles = files;
            Save(true);
        }

        /// <summary>
        /// Returns a copy of this project's conversion override for one repository file, or null when it has none.
        /// </summary>
        public OnnxConversionRecipe GetConversionOverride(string repository, string repoPath)
        {
            return FindConversionOverride(repository, repoPath)?.ToRecipe();
        }

        /// <summary>
        /// Sets or clears (with null) this project's conversion override for one repository file. Returns false when
        /// nothing changed.
        /// </summary>
        public bool SetConversionOverride(string repository, string repoPath, OnnxConversionRecipe recipe)
        {
            var entry = FindConversionOverride(repository, repoPath);
            if (recipe == null)
            {
                if (entry == null)
                {
                    return false;
                }

                _conversionOverrides.Remove(entry);
                Save(true);
                return true;
            }

            var updated = ConversionOverride.From(repository, repoPath, recipe);
            if (entry != null && entry.HasSameValues(updated))
            {
                return false;
            }

            _conversionOverrides ??= new List<ConversionOverride>();
            if (entry != null)
            {
                _conversionOverrides.Remove(entry);
            }

            _conversionOverrides.Add(updated);
            _conversionOverrides.Sort((left, right) =>
            {
                var byRepository = string.CompareOrdinal(left.repository, right.repository);
                return byRepository != 0 ? byRepository : string.CompareOrdinal(left.repoPath, right.repoPath);
            });
            Save(true);
            return true;
        }

        /// <summary>
        /// Starts a test scope without optional-file selections or conversion overrides. Disposing it restores the
        /// project's own entries and writes them back to disk.
        /// </summary>
        internal static IDisposable PushEmptyProjectOverrides()
        {
            var settings = instance;
            var saved = (Selections: settings._optionalFileSelections, Conversions: settings._conversionOverrides);
            settings._optionalFileSelections = new List<OptionalFileSelection>();
            settings._conversionOverrides = new List<ConversionOverride>();
            return new RestoreProjectOverrides(() =>
            {
                settings._optionalFileSelections = saved.Selections;
                settings._conversionOverrides = saved.Conversions;
                settings.Save(true);
            });
        }

        private OptionalFileSelection FindSelection(string ownerId, string modelId)
        {
            return _optionalFileSelections?.FirstOrDefault(item =>
                string.Equals(item?.ownerId, ownerId, StringComparison.Ordinal) &&
                string.Equals(item?.modelId, modelId, StringComparison.Ordinal));
        }

        private ConversionOverride FindConversionOverride(string repository, string repoPath)
        {
            return _conversionOverrides?.FirstOrDefault(item =>
                string.Equals(item?.repository, repository, StringComparison.Ordinal) &&
                string.Equals(item?.repoPath, repoPath, StringComparison.Ordinal));
        }

        /// <summary>
        /// How builds deliver models that have no override: bundled into StreamingAssets, or downloaded at runtime.
        /// </summary>
        public ModelDelivery DefaultDelivery => _defaultDelivery;

        public void SetDefaultDelivery(ModelDelivery delivery)
        {
            _defaultDelivery = delivery;
            Save(true);
        }

        /// <summary>
        /// Returns the delivery override of one repository, or null when it follows the default.
        /// </summary>
        public ModelDelivery? GetDeliveryOverride(string repository)
        {
            var entry = _deliveryOverrides?.FirstOrDefault(item =>
                string.Equals(item?.repository, repository, StringComparison.OrdinalIgnoreCase));
            return entry?.delivery;
        }

        /// <summary>
        /// Sets or clears (with null) the delivery override of one repository.
        /// </summary>
        public void SetDeliveryOverride(string repository, ModelDelivery? delivery)
        {
            _deliveryOverrides ??= new List<DeliveryOverride>();
            _deliveryOverrides.RemoveAll(item => string.Equals(item?.repository, repository, StringComparison.OrdinalIgnoreCase));
            if (delivery.HasValue)
            {
                _deliveryOverrides.Add(new DeliveryOverride { repository = repository, delivery = delivery.Value });
                _deliveryOverrides.Sort((left, right) => string.CompareOrdinal(left.repository, right.repository));
            }

            Save(true);
        }

        public ModelDelivery GetEffectiveDelivery(string repository)
        {
            return GetDeliveryOverride(repository) ?? DefaultDelivery;
        }

        [Serializable]
        private sealed class DeliveryOverride
        {
            public string repository;
            public ModelDelivery delivery;
        }

        [Serializable]
        private sealed class OptionalFileSelection
        {
            public string ownerId;
            public string modelId;
            public List<string> enabledFiles = new();
        }

        [Serializable]
        private sealed class ConversionOverride
        {
            public string repository;
            public string repoPath;
            public string quantization = "none";
            public List<InputShape> inputShapes = new();

            public static ConversionOverride From(string repository, string repoPath, OnnxConversionRecipe recipe)
            {
                return new ConversionOverride
                {
                    repository = repository,
                    repoPath = repoPath,
                    quantization = string.IsNullOrWhiteSpace(recipe.QuantizationMode) ? "none" : recipe.QuantizationMode,
                    inputShapes = (recipe.StaticInputShapeOverrides ?? Array.Empty<OnnxStaticInputShapeOverride>())
                        .Where(item => item != null)
                        .Select(item => new InputShape
                        {
                            input = item.InputName,
                            dimensions = (item.Dimensions ?? Array.Empty<int>()).ToArray()
                        })
                        .ToList()
                };
            }

            public OnnxConversionRecipe ToRecipe()
            {
                return new OnnxConversionRecipe
                {
                    QuantizationMode = string.IsNullOrWhiteSpace(quantization) ? "none" : quantization,
                    StaticInputShapeOverrides = (inputShapes ?? new List<InputShape>())
                        .Where(item => item != null)
                        .Select(item => new OnnxStaticInputShapeOverride
                        {
                            InputName = item.input,
                            Dimensions = (item.dimensions ?? Array.Empty<int>()).ToArray()
                        })
                        .ToArray()
                };
            }

            public bool HasSameValues(ConversionOverride other)
            {
                return string.Equals(quantization, other.quantization, StringComparison.Ordinal) &&
                       inputShapes.Count == other.inputShapes.Count &&
                       inputShapes.Zip(other.inputShapes, (left, right) =>
                               string.Equals(left.input, right.input, StringComparison.Ordinal) &&
                               (left.dimensions ?? Array.Empty<int>()).SequenceEqual(right.dimensions ?? Array.Empty<int>()))
                           .All(same => same);
            }
        }

        [Serializable]
        private sealed class InputShape
        {
            public string input;
            public int[] dimensions = Array.Empty<int>();
        }

        private sealed class RestoreProjectOverrides : IDisposable
        {
            private Action _restore;

            public RestoreProjectOverrides(Action restore)
            {
                _restore = restore;
            }

            public void Dispose()
            {
                _restore?.Invoke();
                _restore = null;
            }
        }
    }
}
