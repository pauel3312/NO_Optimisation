using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NO_OPT.Modules;

internal sealed class ModuleManager(Plugin host, bool headless) : IDisposable
{
    private readonly List<OptimisationModule> _modules = [];
    private readonly ModuleScope _runtimeScope = headless ? ModuleScope.Headless : ModuleScope.Client;
    private bool _clientMasterEnabled = true;
    
    
    public void Dispose()
    {
        for (var i = _modules.Count - 1; i >= 0; i--)
            _modules[i].StopCore();
        _modules.Clear();
    }
    
    internal void Initialise()
    {
        var moduleBase = typeof(OptimisationModule);
        var definitions =
            Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(type => !type.IsAbstract && moduleBase.IsAssignableFrom(type))
                .Select(type => new
                {
                    Type = type,
                    Attribute = type.GetCustomAttribute<OptimisationModuleAttribute>()
                })
                .Where(item => item.Attribute != null && (item.Attribute.Scope & _runtimeScope) != 0)
                .OrderBy(item => item.Attribute!.Order)
                .ThenBy(item => item.Type.FullName, StringComparer.Ordinal)
                .ToArray();
        
        foreach (var definition in definitions)
        {
            OptimisationModule? module = null;
            try
            {
                module = (OptimisationModule)Activator.CreateInstance(definition.Type, true)!;
                module.ConfigureCore(host, definition.Attribute!);
                if (definition.Attribute!.Scope == ModuleScope.Client && definition.Attribute.RespectClientMaster)
                    module.SetManagerEnabledCore(_clientMasterEnabled);
                _modules.Add(module);
            }
            catch (Exception ex)
            {
                module?.StopCore();
                Plugin.LogError($"Failed configuring module {definition.Type.FullName}:\n{ex}");
            }
        }
        
        foreach (var module in _modules)
            module.StartCore();
    }
    
    internal void SetClientMasterEnabled(bool enabled)
    {
        _clientMasterEnabled = enabled;
        foreach (var module in _modules.Where(module => module.Metadata is { Scope: ModuleScope.Client, RespectClientMaster: true }))
        {
            module.SetManagerEnabledCore(enabled);
        }
    }
}