using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Shiron.VulkanDumpster;

/// <summary>
/// Fluent builder that creates a Vulkan instance and optional debug messenger.
/// This is the first Vulkan object every application must create.
/// </summary>
public sealed unsafe class InstanceBuilder : IDisposable {
    private readonly Vk _vk;

    private string _appName = "App";
    private string _engineName = "Engine";
    private Version32 _appVersion = new(1, 0, 0);
    private Version32 _engineVersion = new(1, 0, 0);
    private uint _apiVersion = Vk.Version12;

    private bool _enableValidation;
    private readonly List<string> _extensions = new();
    private readonly List<string> _layers = new();

    private Instance _instance;
    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;

    private bool _built;

    public InstanceBuilder(Vk vk) => _vk = vk ?? throw new ArgumentNullException(nameof(vk));

    public Instance Instance => _instance;

    public InstanceBuilder WithApp(string name, Version32? version = null) {
        _appName = name ?? _appName;
        if (version.HasValue) _appVersion = version.Value;
        return this;
    }

    public InstanceBuilder WithEngine(string name, Version32? version = null) {
        _engineName = name ?? _engineName;
        if (version.HasValue) _engineVersion = version.Value;
        return this;
    }

    public InstanceBuilder WithApiVersion(uint apiVersion) {
        _apiVersion = apiVersion;
        return this;
    }

    /// <summary>
    /// Add required instance extensions (e.g. surface + platform WSI extensions). [web:70]
    /// </summary>
    public InstanceBuilder AddExtensions(params string[] extensions) {
        foreach (var e in extensions.Where(s => !string.IsNullOrWhiteSpace(s)))
            if (!_extensions.Contains(e)) _extensions.Add(e);
        return this;
    }

    public InstanceBuilder EnableValidationLayers(bool enable = true) {
        _enableValidation = enable;
        return this;
    }

    /// <summary>
    /// Override/add instance layers (e.g. "VK_LAYER_KHRONOS_validation"). [web:79]
    /// </summary>
    public InstanceBuilder AddLayers(params string[] layers) {
        foreach (var l in layers.Where(s => !string.IsNullOrWhiteSpace(s)))
            if (!_layers.Contains(l)) _layers.Add(l);
        return this;
    }

    /// <summary>
    /// Build the instance with configured layers/extensions.
    /// When validation is enabled, also creates a debug messenger.
    /// </summary>
    public Instance Build() {
        if (_built) throw new InvalidOperationException("InstanceBuilder.Build() can only be called once.");
        _built = true;

        if (_enableValidation) {
            if (!_layers.Contains("VK_LAYER_KHRONOS_validation"))
                _layers.Add("VK_LAYER_KHRONOS_validation"); // standard validation meta-layer name. [web:79]

            if (!_extensions.Contains(ExtDebugUtils.ExtensionName))
                _extensions.Add(ExtDebugUtils.ExtensionName); // needed for debug messenger. [web:79]
        }

        var appInfo = new ApplicationInfo {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*) SilkMarshal.StringToPtr(_appName, NativeStringEncoding.UTF8),
            ApplicationVersion = _appVersion,
            PEngineName = (byte*) SilkMarshal.StringToPtr(_engineName, NativeStringEncoding.UTF8),
            EngineVersion = _engineVersion,
            ApiVersion = _apiVersion
        };

        var extPtrs = SilkMarshal.StringArrayToPtr(_extensions.ToArray(), NativeStringEncoding.UTF8);
        var layerPtrs = SilkMarshal.StringArrayToPtr(_layers.ToArray(), NativeStringEncoding.UTF8);

        var createInfo = new InstanceCreateInfo {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = (uint) _extensions.Count,
            PpEnabledExtensionNames = (byte**) extPtrs,
            EnabledLayerCount = (uint) _layers.Count,
            PpEnabledLayerNames = (byte**) layerPtrs
        };

        var result = _vk.CreateInstance(&createInfo, null, out _instance);
        SilkMarshal.Free((nint) appInfo.PApplicationName);
        SilkMarshal.Free((nint) appInfo.PEngineName);
        SilkMarshal.Free(extPtrs);
        SilkMarshal.Free(layerPtrs);

        if (result != Result.Success) {
            throw new VulkanException("Failed to create Vulkan instance. " + result, result);
        }

        if (_enableValidation) {
            if (!_vk.TryGetInstanceExtension(_instance, out _debugUtils)) {
                throw new VulkanException("Failed to get debug utils extension.", Result.ErrorExtensionNotPresent);
            }

            var messengerCi = new DebugUtilsMessengerCreateInfoEXT {
                SType = StructureType.DebugUtilsMessengerCreateInfoExt,
                MessageSeverity =
                    DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                    DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                    DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
                MessageType =
                    DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                    DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                    DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
                PfnUserCallback = (PfnDebugUtilsMessengerCallbackEXT) DebugCallback
            };

            var res = _debugUtils!.CreateDebugUtilsMessenger(_instance, &messengerCi, null, out _debugMessenger);
            if (res != Result.Success) {
                throw new VulkanException("Failed to create debug utils messenger.", res);
            }
        }

        return _instance;
    }

    // Keep it minimal: write to stderr.
    private static uint DebugCallback(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT types,
        DebugUtilsMessengerCallbackDataEXT* data,
        void* userData) {
        var msg = SilkMarshal.PtrToString((nint) data->PMessage, NativeStringEncoding.UTF8);
        Console.Error.WriteLine($"[Vulkan] {severity} {types}: {msg}");
        return Vk.False;
    }

    /// <summary>
    /// Destroy the debug messenger and Vulkan instance.
    /// </summary>
    public void Dispose() {
        if (_built) {
            if (_enableValidation && _debugUtils != null && _debugMessenger.Handle != 0) {
                _debugUtils.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
                _debugMessenger = default;
            }

            if (_instance.Handle != 0) {
                _vk.DestroyInstance(_instance, null);
                _instance = default;
            }
        }
    }
}

