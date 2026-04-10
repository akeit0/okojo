namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private void EmitModuleLocalLiveExportEpilogue()
    {
        // Local live exports are now installed by runtime using module slot-backed accessors.
    }

    private bool TryEmitMirrorCurrentLocalStoreToModuleExport(string resolvedName)
    {
        if (!TryGetModuleVariableBinding(resolvedName, out var binding) || binding.IsReadOnly)
            return false;

        EmitStaModuleVariable(binding.CellIndex, binding.Depth);
        return true;
    }

    private bool TryGetModuleVariableBinding(string name, out ModuleVariableBinding binding)
    {
        if (moduleVariableBindings is not null && moduleVariableBindings.TryGetValue(name, out binding))
            return true;
        binding = default;
        return false;
    }
}
