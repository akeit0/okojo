using System.Runtime.CompilerServices;
using Okojo.Bytecode;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandleRuntimeDynamicImport(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        var capability = realm.CreatePromiseCapability(realm.PromiseConstructor);
        try
        {
            var specifier = realm.ToJsStringSlowPath(Unsafe.Add(ref registers, argRegStart));
            string? importType = null;
            if (argCount >= 2 &&
                !TryValidateDynamicImportOptions(realm, capability, Unsafe.Add(ref registers, argRegStart + 1),
                    out importType))
            {
                acc = JsValue.FromObject(capability.Promise);
                return;
            }

            realm.Agent.EnqueueMicrotask(static stateObj =>
            {
                var state = (DynamicImportJobState)stateObj!;
                try
                {
                    var moduleNamespace = state.Realm.Agent.EvaluateModule(
                        state.Realm,
                        state.Specifier,
                        state.Referrer,
                        false,
                        state.ImportType);
                    if (state.Realm.Agent.TryGetPendingModuleEvaluationPromise(state.Specifier, state.Referrer,
                            out var pendingPromise))
                    {
                        var onFulfilled = new JsHostFunction(state.Realm, (in info) =>
                        {
                            info.Realm.ResolvePromiseCapability(state.Capability, moduleNamespace);
                            return JsValue.Undefined;
                        }, string.Empty, 0);
                        var onRejected = new JsHostFunction(state.Realm, (in info) =>
                        {
                            var reason = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
                            info.Realm.RejectPromiseCapability(state.Capability, reason);
                            return JsValue.Undefined;
                        }, string.Empty, 1);
                        _ = state.Realm.PromiseThen(
                            pendingPromise,
                            JsValue.FromObject(onFulfilled),
                            JsValue.FromObject(onRejected));
                    }
                    else
                    {
                        state.Realm.ResolvePromiseCapability(state.Capability, moduleNamespace);
                    }
                }
                catch (JsRuntimeException ex)
                {
                    var reason = ex.ThrownValue ?? state.Realm.CreateErrorObjectFromException(ex);
                    state.Realm.RejectPromiseCapability(state.Capability, reason);
                }
                catch (Exception ex)
                {
                    var wrapped = JsRealm.WrapUnexpectedRuntimeException(ex);
                    var reason = wrapped.ThrownValue ?? state.Realm.CreateErrorObjectFromException(wrapped);
                    state.Realm.RejectPromiseCapability(state.Capability, reason);
                }
            }, new DynamicImportJobState(realm, capability, specifier, script.SourcePath, importType));
        }
        catch (JsRuntimeException ex)
        {
            var reason = ex.ThrownValue ?? realm.CreateErrorObjectFromException(ex);
            realm.RejectPromiseCapability(capability, reason);
        }
        catch (Exception ex)
        {
            var wrapped = JsRealm.WrapUnexpectedRuntimeException(ex);
            var reason = wrapped.ThrownValue ?? realm.CreateErrorObjectFromException(wrapped);
            realm.RejectPromiseCapability(capability, reason);
        }

        acc = JsValue.FromObject(capability.Promise);
    }

    private static bool TryValidateDynamicImportOptions(
        JsRealm realm,
        JsPromiseObject.PromiseCapability capability,
        in JsValue optionsValue,
        out string? importType)
    {
        importType = null;
        if (optionsValue.IsUndefined)
            return true;

        if (!optionsValue.TryGetObject(out var optionsObj))
        {
            RejectDynamicImportTypeError(realm, capability, "Dynamic import options must be an object");
            return false;
        }

        if (!optionsObj.TryGetPropertyByAtom(IdWith, out var attributesValue))
            attributesValue = JsValue.Undefined;
        if (attributesValue.IsUndefined && !optionsObj.TryGetProperty("assert", out attributesValue))
            attributesValue = JsValue.Undefined;

        if (attributesValue.IsUndefined)
            return true;

        if (!attributesValue.TryGetObject(out var attributesObj))
        {
            RejectDynamicImportTypeError(realm, capability, "Dynamic import with option must be an object");
            return false;
        }

        Span<JsValue> keysArgs = [JsValue.FromObject(attributesObj)];
        var keysValue = realm.InvokeObjectConstructorMethod("keys", keysArgs);
        if (!keysValue.TryGetObject(out var keysObj))
        {
            RejectDynamicImportTypeError(realm, capability, "Dynamic import with keys must be an object");
            return false;
        }

        var keyCount = GetArrayLikeLengthLong(realm, keysObj);
        for (long i = 0; i < keyCount; i++)
        {
            _ = keysObj.TryGetElement((uint)i, out var keyValue);
            var keyText = keyValue.IsString ? keyValue.AsString() : realm.ToJsStringSlowPath(keyValue);
            _ = attributesObj.TryGetProperty(keyText, out var attributeValue);
            if (!attributeValue.IsString)
            {
                RejectDynamicImportTypeError(realm, capability,
                    "Dynamic import attribute values must be strings");
                return false;
            }

            if (string.Equals(keyText, "type", StringComparison.Ordinal))
                importType = attributeValue.AsString();
        }

        return true;
    }

    private static void RejectDynamicImportTypeError(
        JsRealm realm,
        JsPromiseObject.PromiseCapability capability,
        string message)
    {
        var error = realm.CreateErrorObjectFromException(new(JsErrorKind.TypeError, message));
        realm.RejectPromiseCapability(capability, error);
    }

    private sealed class DynamicImportJobState(
        JsRealm realm,
        JsPromiseObject.PromiseCapability capability,
        string specifier,
        string? referrer,
        string? importType)
    {
        public readonly JsPromiseObject.PromiseCapability Capability = capability;
        public readonly string? ImportType = importType;
        public readonly JsRealm Realm = realm;
        public readonly string? Referrer = referrer;
        public readonly string Specifier = specifier;
    }
}
