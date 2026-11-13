namespace Okojo.JavaScript.Execution;

public sealed class JsContext
{
    public JsContext(JsContext? parent, int slotCount)
    {
        Parent = parent;
        Slots = new JsValue[slotCount];
        for (var i = 0; i < slotCount; i++)
            Slots[i] = JsValue.Undefined;
        ModuleBindings = parent?.ModuleBindings;
    }

    public JsContext? Parent { get; }
    public JsValue[] Slots { get; }
    internal ModuleExecutionBindings? ModuleBindings { get; set; }
    internal FunctionMetadata? Metadata { get; set; }

    internal sealed class FunctionMetadata
    {
        internal JsValue[]? PrecomputedPrivateMethodValues;
        internal JsBytecodeFunction? ClassInstanceInitializer;
        internal JsObject? PrivateBrandToken;
        internal Dictionary<int, JsObject>? PrivateBrandTokensByBrandId;

        internal FunctionMetadata Clone()
        {
            return new()
            {
                ClassInstanceInitializer = ClassInstanceInitializer,
                PrecomputedPrivateMethodValues = PrecomputedPrivateMethodValues is null
                    ? null
                    : (JsValue[])PrecomputedPrivateMethodValues.Clone(),
                PrivateBrandTokensByBrandId = PrivateBrandTokensByBrandId is null
                    ? null
                    : new Dictionary<int, JsObject>(PrivateBrandTokensByBrandId),
                PrivateBrandToken = PrivateBrandToken,
            };
        }
    }
}
