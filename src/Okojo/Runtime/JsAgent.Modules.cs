using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime.Interop;

namespace Okojo.Runtime;

public sealed partial class JsAgent
{
    private readonly List<ModuleRecordNode> moduleEvaluationStack = new();
    private readonly Stack<ModuleExecutionBindings> moduleRuntimeBindings = new();
    private readonly object moduleRuntimeBindingsGate = new();
    private int nextModuleAsyncEvaluationOrder;

    internal JsValue EvaluateModule(JsRealm realm, string specifier, string? referrer = null,
        bool waitForAsyncCompletion = true, string? importType = null)
    {
        if (!string.IsNullOrEmpty(importType) &&
            !string.Equals(importType, "json", StringComparison.Ordinal) &&
            !string.Equals(importType, "text", StringComparison.Ordinal))
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                $"Unsupported dynamic import type '{importType}'",
                "DYNAMIC_IMPORT_UNSUPPORTED_TYPE");

        if (string.Equals(importType, "json", StringComparison.Ordinal))
        {
            var resolvedImportId = ResolveModuleSpecifierOrThrow(specifier, referrer);
            if (!resolvedImportId.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    $"Dynamic import type 'json' requires a JSON module: '{resolvedImportId}'",
                    "DYNAMIC_IMPORT_JSON_TYPE_MISMATCH");
        }

        if (string.Equals(importType, "text", StringComparison.Ordinal))
            return EvaluateTextModule(realm, specifier, referrer);

        var node = LinkModule(realm, specifier, referrer);
        var resolvedId = node.ResolvedId;
        var linkPlan = node.LinkPlan ?? ModuleLinker.BuildPlan(resolvedId, node.Program);

        if (node.State == ModuleEvalState.Evaluated)
            return node.ExportsObject;
        if (node.State is ModuleEvalState.Instantiating or ModuleEvalState.Evaluating)
        {
            if (waitForAsyncCompletion)
                WaitForPendingTopLevelAwait(node, resolvedId, realm);
            return node.ExportsObject;
        }

        if (node.State == ModuleEvalState.Failed)
        {
            if (node.LastError is not null)
                ExceptionDispatchInfo.Capture(node.LastError).Throw();

            throw new JsRuntimeException(JsErrorKind.TypeError, "Module evaluation previously failed",
                "MODULE_EVAL_FAILED");
        }

        try
        {
            PrepareModuleEvaluation(node, realm, linkPlan);
            StartOrQueueModuleEvaluation(node, realm, resolvedId, linkPlan, waitForAsyncCompletion);
            if (node.State == ModuleEvalState.Failed)
            {
                if (node.LastError is not null)
                    ExceptionDispatchInfo.Capture(node.LastError).Throw();

                throw new JsRuntimeException(JsErrorKind.TypeError, "Module evaluation failed",
                    "MODULE_EVAL_FAILED");
            }

            return node.ExportsObject;
        }
        catch (Exception ex)
        {
            node.State = ModuleEvalState.Failed;
            node.PendingTopLevelAwaitPromise = null;
            node.LastError = ex;
            throw;
        }

        void PrepareModuleEvaluation(ModuleRecordNode targetNode, JsRealm targetRealm, ModuleLinkPlan targetPlan)
        {
            if (targetNode.ExecutionBindings is not null)
                return;

            targetNode.State = ModuleEvalState.Instantiating;
            targetNode.LastError = null;
            moduleEvaluationStack.Add(targetNode);
            try
            {
                var importsObject = new JsPlainObject(targetRealm);
                var importMeta = new JsPlainObject(targetRealm);
                importMeta.DefineDataProperty("url", JsValue.FromString(targetNode.ResolvedId),
                    JsShapePropertyFlags.Open);
                importsObject.DefineDataPropertyAtom(
                    targetRealm,
                    IdOkojoMeta,
                    JsValue.FromObject(importMeta),
                    JsShapePropertyFlags.Open);
                var (moduleVariableBindings, regularExports, regularImports) =
                    BuildModuleVariableSlots(
                        targetPlan.ResolvedImportBindings,
                        targetPlan.ExecutionPlan.ExportLocalByName,
                        targetPlan.ExecutionPlan.PreinitializedLocalExportNames);
                var defaultNameEligibleLocals =
                    CollectDefaultNameEligibleExportLocals(targetPlan.ExecutionPlan.Operations);
                var compileModuleBindings = moduleVariableBindings;
                var setFunctionNameFn = CreateSetFunctionNameHelper(targetRealm);
                var moduleExecutionBindings = new ModuleExecutionBindings(
                    targetNode.ResolvedId,
                    JsValue.FromObject(importsObject),
                    JsValue.Undefined,
                    regularExports,
                    regularImports,
                    JsValue.FromObject(setFunctionNameFn));

                InstallLocalSlotBackedLiveExports(
                    targetRealm,
                    targetNode.ResolvedId,
                    targetNode.ExportsObject,
                    targetPlan.ExecutionPlan.ExportLocalByName,
                    moduleVariableBindings,
                    moduleExecutionBindings,
                    defaultNameEligibleLocals);

                InstantiateHoistedLocalExportFunctions(
                    targetRealm,
                    targetNode.ResolvedId,
                    targetNode.Program.SourceText,
                    targetNode.Program.IdentifierTable,
                    targetPlan.ExecutionPlan,
                    compileModuleBindings,
                    moduleExecutionBindings);

                JsValue EnsureDependencyExports(ResolvedModuleDependency dependency)
                {
                    var cacheKey = GetDependencyCacheKey(dependency.ResolvedId, dependency.ImportType);
                    if (!importsObject.TryGetPropertyAtom(targetRealm, Atoms.InternNoCheck(cacheKey),
                            out var depExports,
                            out _))
                    {
                        depExports = EvaluateModule(targetRealm, dependency.ResolvedId, null, false,
                            dependency.ImportType);
                        if (string.IsNullOrEmpty(dependency.ImportType) &&
                            TryGetActiveDependencyNode(dependency.ResolvedId, out var activeDependencyNode))
                        {
                            activeDependencyNode.AsyncCycleRoot ??= activeDependencyNode;
                            targetNode.AsyncCycleRoot ??= activeDependencyNode.AsyncCycleRoot;
                        }

                        importsObject.DefineDataPropertyAtom(
                            targetRealm,
                            Atoms.InternNoCheck(cacheKey),
                            depExports,
                            JsShapePropertyFlags.Open);
                    }

                    return depExports;
                }

                var linkDiagnostics = new List<ModuleDiagnostic>();

                for (var i = 0; i < targetPlan.RequestedDependencies.Count; i++)
                    _ = EnsureDependencyExports(targetPlan.RequestedDependencies[i]);

                var ambiguousStarNames = ComputeAmbiguousStarExportNames(
                    targetRealm,
                    importsObject,
                    targetPlan.ExecutionPlan.ExportLocalByName,
                    targetPlan.ExportFromBindings,
                    targetPlan.ExportNamespaceFromBindings,
                    targetPlan.ExportStarResolvedIds);

                InstallLinkedReExports(
                    targetRealm,
                    importsObject,
                    targetNode.ExportsObject,
                    targetPlan.ExportFromBindings,
                    targetPlan.ExportNamespaceFromBindings,
                    targetPlan.ExportStarResolvedIds,
                    ambiguousStarNames);

                for (var i = 0; i < targetPlan.ResolvedImportBindings.Count; i++)
                {
                    var binding = targetPlan.ResolvedImportBindings[i];
                    _ = EnsureDependencyExports(new(binding.ResolvedDependencyId, binding.ImportType));
                    if (binding.Kind == ModuleImportBindingKind.Named &&
                        !CanResolveExportName(binding.ResolvedDependencyId, binding.ImportedName, binding.ImportType))
                        linkDiagnostics.Add(ModuleLinker.CreateDiagnostic(
                            "MODULE_IMPORT_NAME_NOT_FOUND",
                            targetNode.ResolvedId,
                            targetNode.Program,
                            binding.Position,
                            $"Module '{targetNode.ResolvedId}' imports '{binding.ImportedName}' from '{binding.ResolvedDependencyId}', but it is not exported."));
                }

                for (var i = 0; i < targetPlan.ExportFromBindings.Count; i++)
                {
                    var from = targetPlan.ExportFromBindings[i];
                    _ = EnsureDependencyExports(new(from.ResolvedDependencyId, from.ImportType));
                    if (!CanResolveExportName(from.ResolvedDependencyId, from.ImportedName, from.ImportType))
                        linkDiagnostics.Add(ModuleLinker.CreateDiagnostic(
                            "MODULE_EXPORT_NAME_NOT_FOUND",
                            targetNode.ResolvedId,
                            targetNode.Program,
                            from.Position,
                            $"Module '{targetNode.ResolvedId}' re-exports '{from.ImportedName}' from '{from.ResolvedDependencyId}', but it is not exported."));
                }

                if (linkDiagnostics.Count != 0)
                    throw WrapModuleLinkException(targetNode.ResolvedId,
                        ModuleLinker.ToRuntimeException(linkDiagnostics[0]));
                targetNode.ExportsObject.LockForRuntimeMutation();
                targetNode.ExecutionBindings = moduleExecutionBindings;
                targetNode.CompileModuleBindings = compileModuleBindings;
                targetNode.RequiresTopLevelAwait = targetPlan.ExecutionPlan.RequiresTopLevelAwait;
                targetNode.State = ModuleEvalState.Uninitialized;
            }
            finally
            {
                _ = moduleEvaluationStack.Remove(targetNode);
            }
        }

        static void InstantiateHoistedLocalExportFunctions(
            JsRealm targetRealm,
            string moduleResolvedId,
            string? moduleSourceText,
            JsIdentifierTable? moduleIdentifierTable,
            ModuleExecutionPlan executionPlan,
            IReadOnlyDictionary<string, ModuleVariableBinding>? compileModuleBindings,
            ModuleExecutionBindings moduleExecutionBindings)
        {
            if (compileModuleBindings is null || compileModuleBindings.Count == 0)
                return;
            if (!HasHoistedLocalExportFunctionsToInstantiate(executionPlan, compileModuleBindings))
                return;

            JsCompiler? compiler = null;
            JsContext? moduleFunctionContext = null;
            try
            {
                compiler = JsCompiler.CreateForModuleExecution(targetRealm, compileModuleBindings);
                var environment = compiler.DescribeModuleExecutionEnvironment(executionPlan, moduleIdentifierTable);
                moduleFunctionContext = CreateTopLevelModuleContext(environment, moduleExecutionBindings);
                moduleExecutionBindings.TopLevelContext = moduleFunctionContext;

                for (var i = 0; i < executionPlan.Operations.Count; i++)
                    TryInstantiateHoistedLocalExportFunction(executionPlan.Operations[i]);

                void TryInstantiateHoistedLocalExportFunction(ModuleExecutionOp op)
                {
                    JsBytecodeFunction? template = null;
                    var exportIndex = -1;

                    switch (op.Kind)
                    {
                        case ModuleExecutionOpKind.ExecuteStatement
                            when op.Statement is JsFunctionDeclaration declaration &&
                                 compileModuleBindings.TryGetValue(declaration.Name, out var namedBinding) &&
                                 namedBinding.CellIndex > 0:
                            exportIndex = namedBinding.CellIndex - 1;
                            template = compiler.CompileHoistedFunctionTemplate(
                                declaration,
                                moduleSourceText,
                                moduleResolvedId,
                                moduleIdentifierTable);
                            break;

                        case ModuleExecutionOpKind.InitializeHoistedDefaultExport
                            when op.Expression is JsFunctionExpression declaration &&
                                 !string.IsNullOrEmpty(op.ExportLocalName) &&
                                 compileModuleBindings.TryGetValue(op.ExportLocalName, out var defaultBinding) &&
                                 defaultBinding.CellIndex > 0:
                            exportIndex = defaultBinding.CellIndex - 1;
                            template = compiler.CompileHoistedFunctionTemplate(
                                declaration,
                                "default",
                                moduleSourceText,
                                moduleResolvedId,
                                moduleIdentifierTable);
                            break;
                    }

                    if (template is null || (uint)exportIndex >= (uint)moduleExecutionBindings.RegularExports.Length)
                        return;

                    var slot = moduleExecutionBindings.RegularExports[exportIndex];
                    if (!slot.LocalValue.IsTheHole)
                        return;

                    var closure = template.CloneForClosure(targetRealm);
                    closure.BoundParentContext = moduleFunctionContext;
                    slot.LocalValue = JsValue.FromObject(closure);
                }
            }
            finally
            {
                compiler?.Dispose();
            }
        }

        static bool HasHoistedLocalExportFunctionsToInstantiate(
            ModuleExecutionPlan executionPlan,
            IReadOnlyDictionary<string, ModuleVariableBinding> compileModuleBindings)
        {
            for (var i = 0; i < executionPlan.Operations.Count; i++)
            {
                var op = executionPlan.Operations[i];
                if (op.Kind == ModuleExecutionOpKind.ExecuteStatement &&
                    op.Statement is JsFunctionDeclaration declaration &&
                    compileModuleBindings.TryGetValue(declaration.Name, out var binding) &&
                    binding.CellIndex > 0)
                    return true;

                if (op.Kind == ModuleExecutionOpKind.InitializeHoistedDefaultExport &&
                    op.Expression is JsFunctionExpression &&
                    !string.IsNullOrEmpty(op.ExportLocalName) &&
                    compileModuleBindings.TryGetValue(op.ExportLocalName, out binding) &&
                    binding.CellIndex > 0)
                    return true;
            }

            return false;
        }

        static JsContext CreateTopLevelModuleContext(
            ModuleExecutionEnvironment environment,
            ModuleExecutionBindings moduleExecutionBindings)
        {
            var context = new JsContext(null, environment.SlotCount)
            {
                ModuleBindings = moduleExecutionBindings
            };
            for (var i = 0; i < environment.InitialSlotValues.Length; i++)
                context.Slots[i] = environment.InitialSlotValues[i];
            return context;
        }

        void StartOrQueueModuleEvaluation(ModuleRecordNode targetNode, JsRealm targetRealm, string targetResolvedId,
            ModuleLinkPlan targetPlan, bool shouldWait)
        {
            if (targetNode.EvaluationStarted)
            {
                if (shouldWait)
                    WaitForPendingTopLevelAwait(targetNode, targetResolvedId, targetRealm);
                return;
            }

            RegisterPendingImportDependencies(targetNode, targetRealm, targetPlan.RequestedDependencies);
            if (targetNode.PendingAsyncDependencies > 0)
            {
                EnsureAsyncEvaluationState(targetNode, targetRealm);
                targetNode.State = ModuleEvalState.Evaluating;
                if (shouldWait)
                    WaitForPendingTopLevelAwait(targetNode, targetResolvedId, targetRealm);
                return;
            }

            StartModuleExecution(targetNode, targetRealm, targetResolvedId, targetPlan, shouldWait);
        }

        void StartModuleExecution(ModuleRecordNode targetNode, JsRealm targetRealm, string targetResolvedId,
            ModuleLinkPlan targetPlan, bool shouldWait)
        {
            if (targetNode.EvaluationStarted)
                return;

            targetNode.State = ModuleEvalState.Evaluating;
            targetNode.EvaluationStarted = true;
            if (targetNode.RequiresTopLevelAwait)
                EnsureAsyncEvaluationState(targetNode, targetRealm);
            EnsureModuleExplicitResourceStack(targetNode, targetRealm, targetPlan.ExecutionPlan);

            PushModuleRuntimeBindings(targetNode.ExecutionBindings!);
            try
            {
                JsValue executionResult;
                try
                {
                    executionResult = ModuleExecutor.ExecuteProgram(
                        targetRealm,
                        targetResolvedId,
                        targetNode.Program.SourceText,
                        targetNode.Program.IdentifierTable,
                        targetPlan.ExecutionPlan,
                        targetNode.CompileModuleBindings,
                        false);
                }
                catch (Exception ex)
                {
                    var wrapped = WrapModuleExecutionException(targetResolvedId, ex);
                    FinalizeModuleExecution(targetNode, targetResolvedId, targetRealm,
                        wrapped.ThrownValue ?? targetRealm.CreateErrorObjectFromException(wrapped),
                        hasAbruptCompletion: true, wrapped);
                    if (shouldWait)
                    {
                        if (wrapped == ex) throw;
                        throw wrapped;
                    }

                    return;
                }

                if (targetNode.RequiresTopLevelAwait &&
                    executionResult.TryGetObject(out var executionObj) &&
                    executionObj is JsPromiseObject executionPromise)
                {
                    AttachModuleExecutionPromise(targetNode, targetResolvedId, targetRealm, executionPromise);
                    if (shouldWait)
                        WaitForPendingTopLevelAwait(targetNode, targetResolvedId, targetRealm);
                    return;
                }

                FinalizeModuleExecution(targetNode, targetResolvedId, targetRealm, JsValue.Undefined,
                    hasAbruptCompletion: false, null);
                if (shouldWait)
                    WaitForPendingTopLevelAwait(targetNode, targetResolvedId, targetRealm);
            }
            finally
            {
                PopModuleRuntimeBindings();
            }
        }

        void AttachModuleExecutionPromise(ModuleRecordNode targetNode, string targetResolvedId, JsRealm targetRealm,
            JsPromiseObject executionPromise)
        {
            var onFulfilled = new JsHostFunction(targetRealm, (in info) =>
            {
                FinalizeModuleExecution(targetNode, targetResolvedId, info.Realm, JsValue.Undefined,
                    hasAbruptCompletion: false, null);
                return JsValue.Undefined;
            }, string.Empty, 0);
            var onRejected = new JsHostFunction(targetRealm, (in info) =>
            {
                var reason = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
                FinalizeModuleExecution(targetNode, targetResolvedId, info.Realm, reason, hasAbruptCompletion: true,
                    null);
                return JsValue.Undefined;
            }, string.Empty, 1);
            _ = targetRealm.PromiseThen(
                executionPromise,
                JsValue.FromObject(onFulfilled),
                JsValue.FromObject(onRejected));
        }

        void FinalizeModuleExecution(ModuleRecordNode targetNode, string targetResolvedId, JsRealm targetRealm,
            in JsValue completionValue, bool hasAbruptCompletion, JsRuntimeException? abruptFailure)
        {
            var stack = targetNode.ExecutionBindings?.ExplicitResourceStack;
            if (stack is null || stack.IsDisposed)
            {
                if (hasAbruptCompletion)
                    FailModuleEvaluation(targetNode, targetResolvedId, targetRealm, completionValue, abruptFailure);
                else
                    CompleteModuleEvaluation(targetNode, targetRealm);
                return;
            }

            JsValue cleanupResult;
            try
            {
                cleanupResult = targetRealm.Intrinsics.DisposeCompilerDisposableStack(
                    stack,
                    hasAbruptCompletion ? 2 : 0,
                    completionValue);
            }
            catch (Exception ex)
            {
                var cleanupReason = GetModuleCleanupExceptionReason(targetRealm, ex);
                var cleanupFailure = ex as JsRuntimeException ??
                                     CreateModuleAwaitRejectedException(targetResolvedId, cleanupReason);
                FailModuleEvaluation(targetNode, targetResolvedId, targetRealm, cleanupReason, cleanupFailure);
                return;
            }

            if (cleanupResult.TryGetObject(out var cleanupObj) && cleanupObj is JsPromiseObject cleanupPromise)
            {
                EnsureAsyncEvaluationState(targetNode, targetRealm);
                AttachModuleCleanupPromise(targetNode, targetResolvedId, targetRealm, cleanupPromise,
                    completionValue, hasAbruptCompletion, abruptFailure);
                return;
            }

            if (hasAbruptCompletion)
                FailModuleEvaluation(targetNode, targetResolvedId, targetRealm, completionValue, abruptFailure);
            else
                CompleteModuleEvaluation(targetNode, targetRealm);
        }

        void AttachModuleCleanupPromise(ModuleRecordNode targetNode, string targetResolvedId, JsRealm targetRealm,
            JsPromiseObject cleanupPromise, JsValue completionValue, bool hasAbruptCompletion,
            JsRuntimeException? abruptFailure)
        {
            var onFulfilled = new JsHostFunction(targetRealm, (in info) =>
            {
                if (hasAbruptCompletion)
                    FailModuleEvaluation(targetNode, targetResolvedId, info.Realm, completionValue, abruptFailure);
                else
                    CompleteModuleEvaluation(targetNode, info.Realm);
                return JsValue.Undefined;
            }, string.Empty, 0);
            var onRejected = new JsHostFunction(targetRealm, (in info) =>
            {
                var reason = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
                FailModuleEvaluation(targetNode, targetResolvedId, info.Realm, reason, null);
                return JsValue.Undefined;
            }, string.Empty, 1);
            _ = targetRealm.PromiseThen(
                cleanupPromise,
                JsValue.FromObject(onFulfilled),
                JsValue.FromObject(onRejected));
        }

        void EnsureModuleExplicitResourceStack(ModuleRecordNode targetNode, JsRealm targetRealm,
            ModuleExecutionPlan executionPlan)
        {
            if (!executionPlan.HasTopLevelUsingLike)
                return;

            var bindings = targetNode.ExecutionBindings!;
            if (bindings.ExplicitResourceStack is null)
                bindings.ExplicitResourceStack =
                    targetRealm.Intrinsics.CreateCompilerDisposableStack(executionPlan.HasTopLevelAwaitUsingLike);
        }

        void CompleteModuleEvaluation(ModuleRecordNode targetNode, JsRealm targetRealm)
        {
            if (targetNode.State == ModuleEvalState.Evaluated)
                return;
            if (targetNode.State == ModuleEvalState.Failed)
                return;

            var completionPromise = targetNode.PendingTopLevelAwaitPromise;
            targetNode.PendingTopLevelAwaitPromise = null;
            targetNode.PendingAsyncDependencies = 0;
            targetNode.State = ModuleEvalState.Evaluated;
            targetNode.LastError = null;
            targetNode.EvaluationStarted = true;
            if (completionPromise is not null)
                targetRealm.ResolvePromise(completionPromise, JsValue.Undefined);

            var readyAncestors = GatherReadyAncestors(targetNode);
            if (readyAncestors.Count == 0)
                return;

            readyAncestors.Sort(static (left, right) =>
                left.AsyncEvaluationOrder.CompareTo(right.AsyncEvaluationOrder));
            for (var i = 0; i < readyAncestors.Count; i++)
            {
                var ancestor = readyAncestors[i];
                targetRealm.Agent.EnqueueMicrotask(static stateObj => ((Action)stateObj!).Invoke(),
                    (Action)(() =>
                    {
                        var ancestorPlan = ancestor.LinkPlan ??
                                           ModuleLinker.BuildPlan(ancestor.ResolvedId, ancestor.Program);
                        StartModuleExecution(ancestor, targetRealm, ancestor.ResolvedId, ancestorPlan, false);
                    }));
            }
        }

        void FailModuleEvaluation(ModuleRecordNode targetNode, string targetResolvedId, JsRealm targetRealm,
            in JsValue reason, JsRuntimeException? failure)
        {
            if (targetNode.State == ModuleEvalState.Failed)
                return;
            if (targetNode.State == ModuleEvalState.Evaluated)
                return;

            targetNode.State = ModuleEvalState.Failed;
            targetNode.PendingAsyncDependencies = 0;
            targetNode.LastError = failure ?? CreateModuleAwaitRejectedException(targetResolvedId, reason);
            targetNode.EvaluationStarted = true;
            var completionPromise = targetNode.PendingTopLevelAwaitPromise;
            targetNode.PendingTopLevelAwaitPromise = null;
            if (completionPromise is not null)
                targetRealm.RejectPromise(completionPromise, reason);

            if (targetNode.AsyncParentModules.Count == 0)
                return;

            var parents = new List<ModuleRecordNode>(targetNode.AsyncParentModules);
            targetNode.AsyncParentModules.Clear();
            parents.Sort(static (left, right) => left.AsyncEvaluationOrder.CompareTo(right.AsyncEvaluationOrder));
            var rejectionReason = reason;
            for (var i = 0; i < parents.Count; i++)
            {
                var parent = parents[i];
                targetRealm.Agent.EnqueueMicrotask(static stateObj => ((Action)stateObj!).Invoke(),
                    (Action)(() =>
                        FailModuleEvaluation(parent, parent.ResolvedId, targetRealm, rejectionReason, null)));
            }
        }

        List<ModuleRecordNode> GatherReadyAncestors(ModuleRecordNode completedNode)
        {
            var ready = new List<ModuleRecordNode>();
            if (completedNode.AsyncParentModules.Count == 0)
                return ready;

            var parents = new List<ModuleRecordNode>(completedNode.AsyncParentModules);
            completedNode.AsyncParentModules.Clear();
            for (var i = 0; i < parents.Count; i++)
            {
                var parent = parents[i];
                if (parent.PendingAsyncDependencies > 0)
                    parent.PendingAsyncDependencies--;
                if (parent.PendingAsyncDependencies == 0 &&
                    parent.State == ModuleEvalState.Evaluating &&
                    !parent.EvaluationStarted)
                    ready.Add(parent);
            }

            return ready;
        }

        void RegisterPendingImportDependencies(ModuleRecordNode targetNode, JsRealm targetRealm,
            IReadOnlyList<ResolvedModuleDependency> dependencies)
        {
            for (var i = 0; i < dependencies.Count; i++)
            {
                if (!string.IsNullOrEmpty(dependencies[i].ImportType))
                    continue;

                ModuleRecordNode? dependencyNode = null;
                lock (moduleCacheGate)
                {
                    _ = ModuleGraph.TryGet(dependencies[i].ResolvedId, out dependencyNode);
                }

                var dependencyWaitNode = GetPendingDependencyWaitNode(targetNode, dependencyNode);
                if (dependencyWaitNode is null)
                    continue;
                if (ReferenceEquals(dependencyWaitNode, targetNode))
                    continue;
                if (dependencyWaitNode.AsyncParentModules.Contains(targetNode))
                    continue;

                dependencyWaitNode.AsyncParentModules.Add(targetNode);
                targetNode.PendingAsyncDependencies++;
                EnsureAsyncEvaluationState(targetNode, targetRealm);
            }
        }

        void EnsureAsyncEvaluationState(ModuleRecordNode targetNode, JsRealm targetRealm)
        {
            if (targetNode.PendingTopLevelAwaitPromise is null)
                targetNode.PendingTopLevelAwaitPromise = targetRealm.CreatePromiseObject();
            if (targetNode.AsyncEvaluationOrder == 0)
                targetNode.AsyncEvaluationOrder = ++nextModuleAsyncEvaluationOrder;
        }

        static void WaitForPendingTopLevelAwait(ModuleRecordNode targetNode, string targetResolvedId,
            JsRealm targetRealm)
        {
            if (targetNode.PendingTopLevelAwaitPromise is null)
            {
                if (targetNode.State == ModuleEvalState.Failed && targetNode.LastError is not null)
                    ExceptionDispatchInfo.Capture(targetNode.LastError).Throw();
                return;
            }

            var promise = targetNode.PendingTopLevelAwaitPromise;
            while (promise.State == JsPromiseObject.PromiseState.Pending)
                targetRealm.Agent.PumpJobs();

            if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
                return;

            if (targetNode.LastError is not null)
                ExceptionDispatchInfo.Capture(targetNode.LastError).Throw();

            throw CreateModuleAwaitRejectedException(targetResolvedId, promise.Result);
        }

        bool TryGetActiveDependencyNode(string dependencyResolvedId, out ModuleRecordNode activeDependencyNode)
        {
            for (var i = moduleEvaluationStack.Count - 1; i >= 0; i--)
            {
                var candidate = moduleEvaluationStack[i];
                if (string.Equals(candidate.ResolvedId, dependencyResolvedId, StringComparison.Ordinal))
                {
                    activeDependencyNode = candidate;
                    return true;
                }
            }

            activeDependencyNode = null!;
            return false;
        }

        ModuleRecordNode? GetPendingDependencyWaitNode(ModuleRecordNode targetNode,
            ModuleRecordNode? dependencyNode)
        {
            if (dependencyNode is null)
                return null;

            var cycleRoot = dependencyNode.AsyncCycleRoot;
            var sharesAsyncCycle = cycleRoot is not null &&
                                   CanReachModule(targetNode, cycleRoot) &&
                                   CanReachModule(cycleRoot, targetNode);
            if (sharesAsyncCycle && ReferenceEquals(dependencyNode, cycleRoot))
                return null;

            if (cycleRoot is not null &&
                !sharesAsyncCycle &&
                cycleRoot.State != ModuleEvalState.Evaluated &&
                cycleRoot.State != ModuleEvalState.Failed)
                return cycleRoot;

            if (dependencyNode.State == ModuleEvalState.Evaluated ||
                dependencyNode.State == ModuleEvalState.Failed)
                return null;

            return dependencyNode;
        }

        bool CanReachModule(ModuleRecordNode startNode, ModuleRecordNode targetNode)
        {
            if (ReferenceEquals(startNode, targetNode))
                return true;

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<ModuleRecordNode>();
            stack.Push(startNode);
            while (stack.Count != 0)
            {
                var current = stack.Pop();
                if (!visited.Add(current.ResolvedId))
                    continue;

                var dependencies = ModuleGraph.GetDependencies(current);
                for (var i = 0; i < dependencies.Count; i++)
                {
                    var dependency = dependencies[i];
                    if (ReferenceEquals(dependency, targetNode))
                        return true;

                    if (!visited.Contains(dependency.ResolvedId))
                        stack.Push(dependency);
                }
            }

            return false;
        }
    }

    internal JsValue EvaluateJsonModule(JsRealm realm, string specifier, string? referrer = null,
        bool requireJsonType = false)
    {
        var resolvedId = ResolveModuleSpecifierOrThrow(specifier, referrer);
        if (requireJsonType && !resolvedId.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                $"Dynamic import type 'json' requires a JSON module: '{resolvedId}'",
                "DYNAMIC_IMPORT_JSON_TYPE_MISMATCH");
        lock (moduleCacheGate)
        {
            if (jsonModuleNamespaceCache.TryGetValue(resolvedId, out var cachedNamespace))
                return cachedNamespace;
        }

        var source = LoadModuleSourceByResolvedIdOrThrow(resolvedId);
        var defaultExport = realm.ParseJsonModuleSource(source);
        var moduleNamespace = new JsModuleNamespaceObject(realm);
        moduleNamespace.DefineDataPropertyAtom(realm, realm.Atoms.InternNoCheck("default"), defaultExport,
            JsShapePropertyFlags.Open);
        moduleNamespace.LockForRuntimeMutation();

        lock (moduleCacheGate)
        {
            if (!jsonModuleNamespaceCache.TryGetValue(resolvedId, out var cachedNamespace))
                jsonModuleNamespaceCache.Add(resolvedId, moduleNamespace);
            else
                moduleNamespace = cachedNamespace;
        }

        return moduleNamespace;
    }

    internal JsValue EvaluateTextModule(JsRealm realm, string specifier, string? referrer = null)
    {
        var resolvedId = ResolveModuleSpecifierOrThrow(specifier, referrer);
        lock (moduleCacheGate)
        {
            if (textModuleNamespaceCache.TryGetValue(resolvedId, out var cachedNamespace))
                return cachedNamespace;
        }

        var source = LoadModuleSourceByResolvedIdOrThrow(resolvedId);
        var moduleNamespace = new JsModuleNamespaceObject(realm);
        moduleNamespace.DefineDataPropertyAtom(realm, realm.Atoms.InternNoCheck("default"), JsValue.FromString(source),
            JsShapePropertyFlags.Open);
        moduleNamespace.LockForRuntimeMutation();

        lock (moduleCacheGate)
        {
            if (!textModuleNamespaceCache.TryGetValue(resolvedId, out var cachedNamespace))
                textModuleNamespaceCache.Add(resolvedId, moduleNamespace);
            else
                moduleNamespace = cachedNamespace;
        }

        return moduleNamespace;
    }

    internal ModuleRecordNode LinkModule(JsRealm realm, string specifier, string? referrer)
    {
        var rootResolvedId = ResolveModuleSpecifierOrThrow(specifier, referrer);
        try
        {
            ModuleRecordNode? rootNode = null;
            var inPath = new HashSet<string>(StringComparer.Ordinal);
            var linked = new HashSet<string>(StringComparer.Ordinal);

            LinkByResolvedId(rootResolvedId);
            return rootNode!;

            void LinkByResolvedId(string resolvedId)
            {
                if (!linked.Add(resolvedId))
                    return;
                if (!inPath.Add(resolvedId))
                    return;

                ModuleRecordNode node;
                ModuleLinkPlan? plan;
                lock (moduleCacheGate)
                {
                    var source = LoadModuleSourceByResolvedIdOrThrow(resolvedId);
                    node = GetOrCreateModuleNodeOrThrow(resolvedId, source, realm);
                    plan = node.LinkPlan;
                }

                if (plan is null)
                {
                    var linkResult = ModuleLinker.BuildPlanResult(resolvedId, node.Program);
                    if (linkResult.Diagnostics.Count != 0)
                        throw WrapModuleLinkException(resolvedId,
                            ModuleLinker.ToRuntimeException(linkResult.Diagnostics[0]));
                    lock (moduleCacheGate)
                    {
                        if (node.LinkPlan is null)
                            node.LinkPlan = linkResult.Plan;
                        plan = node.LinkPlan;
                    }
                }

                if (string.Equals(resolvedId, rootResolvedId, StringComparison.Ordinal))
                    rootNode = node;

                foreach (var dependency in EnumerateLinkDependencies(plan!))
                {
                    if (!string.IsNullOrEmpty(dependency.ImportType))
                        continue;
                    LinkByResolvedId(dependency.ResolvedId);
                }

                _ = inPath.Remove(resolvedId);
            }
        }
        catch (JsRuntimeException ex)
        {
            throw WrapModuleLinkException(rootResolvedId, ex);
        }
    }

    private static IEnumerable<ResolvedModuleDependency> EnumerateLinkDependencies(ModuleLinkPlan plan)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < plan.RequestedDependencies.Count; i++)
        {
            var dep = plan.RequestedDependencies[i];
            var key = GetDependencyCacheKey(dep.ResolvedId, dep.ImportType);
            if (seen.Add(key))
                yield return dep;
        }
    }

    internal bool TryGetPendingModuleEvaluationPromise(string specifier, string? referrer,
        out JsPromiseObject pendingPromise)
    {
        var resolvedId = ResolveModuleSpecifierOrThrow(specifier, referrer);
        lock (moduleCacheGate)
        {
            if (ModuleGraph.TryGet(resolvedId, out var node))
            {
                var waitNode = node.PendingTopLevelAwaitPromise is not null
                    ? node
                    : node.AsyncCycleRoot?.PendingTopLevelAwaitPromise is not null
                        ? node.AsyncCycleRoot
                        : node;
                if (waitNode.PendingTopLevelAwaitPromise is not null)
                {
                    pendingPromise = waitNode.PendingTopLevelAwaitPromise;
                    return true;
                }
            }
        }

        pendingPromise = null!;
        return false;
    }

    private static JsRuntimeException CreateModuleAwaitRejectedException(string resolvedId, in JsValue reason)
    {
        return new(
            JsErrorKind.TypeError,
            $"Top-level await module '{resolvedId}' rejected",
            "MODULE_TOP_LEVEL_AWAIT_REJECTED",
            reason);
    }

    private static JsValue GetModuleCleanupExceptionReason(JsRealm realm, Exception ex)
    {
        return ex switch
        {
            PromiseRejectedException promiseRejected => promiseRejected.Reason,
            JsRuntimeException runtime => runtime.ThrownValue ?? realm.CreateErrorObjectFromException(runtime),
            _ => realm.CreateErrorObjectFromException(new JsRuntimeException(
                JsErrorKind.InternalError,
                ex.Message,
                null,
                null,
                ex))
        };
    }

    internal string ResolveModuleSpecifierOrThrow(string specifier, string? referrer)
    {
        try
        {
            return Engine.ModuleSourceLoader.ResolveSpecifier(specifier, referrer);
        }
        catch (JsRuntimeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var from = referrer ?? "<root>";
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                $"Failed to resolve module specifier '{specifier}' from '{from}': {ex.Message}",
                "MODULE_RESOLVE_FAILED",
                innerException: ex);
        }
    }

    private string LoadModuleSourceByResolvedIdOrThrow(string resolvedId)
    {
        try
        {
            return LoadModuleSourceByResolvedId(resolvedId);
        }
        catch (JsRuntimeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                $"Failed to load module '{resolvedId}': {ex.Message}",
                "MODULE_LOAD_FAILED",
                innerException: ex);
        }
    }

    private ModuleRecordNode GetOrCreateModuleNodeOrThrow(string resolvedId, string source, JsRealm realm)
    {
        try
        {
            return ModuleGraph.GetOrCreate(
                resolvedId,
                PrepareSourceForModuleParsing(resolvedId, source, realm),
                new(realm));
        }
        catch (JsParseException ex)
        {
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                $"Failed to parse module '{resolvedId}': {ex.Message}",
                "MODULE_PARSE_FAILED",
                innerException: ex);
        }
    }

    private static string PrepareSourceForModuleParsing(string resolvedId, string source, JsRealm realm)
    {
        if (!resolvedId.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return source;

        _ = realm.ParseJsonModuleSource(source);
        return $"export default ({source});";
    }

    private static JsRuntimeException WrapModuleExecutionException(string resolvedId, Exception ex)
    {
        if (ex is JsRuntimeException runtimeEx)
        {
            if (!string.IsNullOrEmpty(runtimeEx.DetailCode) || runtimeEx.InnerException is null)
                return runtimeEx;

            return new(
                JsErrorKind.TypeError,
                $"Failed to evaluate module '{resolvedId}': {runtimeEx.InnerException.Message}",
                "MODULE_EXEC_FAILED",
                innerException: runtimeEx.InnerException);
        }

        return new(
            JsErrorKind.TypeError,
            $"Failed to evaluate module '{resolvedId}': {ex.Message}",
            "MODULE_EXEC_FAILED",
            innerException: ex);
    }

    private static JsRuntimeException WrapModuleLinkException(string resolvedId, Exception ex)
    {
        if (ex is JsRuntimeException runtimeEx)
        {
            if (runtimeEx.DetailCode is "MODULE_RESOLVE_FAILED" or "MODULE_LOAD_FAILED" or "MODULE_PARSE_FAILED")
                return runtimeEx;
            if (runtimeEx.DetailCode == "MODULE_LINK_FAILED")
                return runtimeEx;

            return new(
                runtimeEx.Kind,
                $"Failed to link module '{resolvedId}': {runtimeEx.Message}",
                "MODULE_LINK_FAILED",
                innerException: runtimeEx);
        }

        return new(
            JsErrorKind.TypeError,
            $"Failed to link module '{resolvedId}': {ex.Message}",
            "MODULE_LINK_FAILED",
            innerException: ex);
    }

    private bool CanResolveExportName(string moduleResolvedId, string exportName, string? importType = null)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return TryResolvePlannedExportBindingIdentity(moduleResolvedId, exportName, importType, visited, out _,
                   out var ambiguous) &&
               !ambiguous;
    }

    private bool TryResolvePlannedExportBindingIdentity(
        string moduleResolvedId,
        string exportName,
        string? importType,
        HashSet<string> visited,
        out ExportBindingIdentity identity,
        out bool ambiguous)
    {
        identity = default;
        ambiguous = false;
        if (string.Equals(importType, "text", StringComparison.Ordinal))
        {
            if (string.Equals(exportName, "default", StringComparison.Ordinal))
            {
                identity = ExportBindingIdentity.Named(moduleResolvedId, "default");
                return true;
            }

            return false;
        }

        if (string.Equals(exportName, "default", StringComparison.Ordinal))
        {
            // `default` never resolves through star exports.
        }

        var visitKey = GetDependencyCacheKey(moduleResolvedId, importType) + ":" + exportName;
        if (!visited.Add(visitKey))
            return false;

        if (!ModuleGraph.TryGet(moduleResolvedId, out var node))
            return false;
        var plan = node.LinkPlan ?? ModuleLinker.BuildPlan(moduleResolvedId, node.Program);

        if (plan.ExecutionPlan.ExportLocalByName.TryGetValue(exportName, out var localName))
        {
            for (var i = 0; i < plan.ResolvedImportBindings.Count; i++)
            {
                var binding = plan.ResolvedImportBindings[i];
                if (!string.Equals(binding.LocalName, localName, StringComparison.Ordinal))
                    continue;

                if (binding.Kind == ModuleImportBindingKind.Namespace)
                {
                    identity = ExportBindingIdentity.Namespace(binding.ResolvedDependencyId, binding.ImportType);
                    return true;
                }

                return TryResolvePlannedExportBindingIdentity(
                    binding.ResolvedDependencyId,
                    binding.ImportedName,
                    binding.ImportType,
                    visited,
                    out identity,
                    out ambiguous);
            }

            identity = ExportBindingIdentity.Named(moduleResolvedId, localName);
            return true;
        }

        for (var i = 0; i < plan.ExportNamespaceFromBindings.Count; i++)
        {
            var binding = plan.ExportNamespaceFromBindings[i];
            if (!string.Equals(binding.ExportedName, exportName, StringComparison.Ordinal))
                continue;
            identity = ExportBindingIdentity.Namespace(binding.ResolvedDependencyId, binding.ImportType);
            return true;
        }

        for (var i = 0; i < plan.ExportFromBindings.Count; i++)
        {
            var binding = plan.ExportFromBindings[i];
            if (!string.Equals(binding.ExportedName, exportName, StringComparison.Ordinal))
                continue;
            return TryResolvePlannedExportBindingIdentity(
                binding.ResolvedDependencyId,
                binding.ImportedName,
                binding.ImportType,
                visited,
                out identity,
                out ambiguous);
        }

        if (string.Equals(exportName, "default", StringComparison.Ordinal))
            return false;

        var foundStar = false;
        ExportBindingIdentity starIdentity = default;
        for (var i = 0; i < plan.ExportStarResolvedIds.Count; i++)
        {
            if (!TryResolvePlannedExportBindingIdentity(
                    plan.ExportStarResolvedIds[i],
                    exportName,
                    null,
                    visited,
                    out var candidate,
                    out var candidateAmbiguous))
                continue;

            if (candidateAmbiguous)
            {
                ambiguous = true;
                return false;
            }

            if (!foundStar)
            {
                foundStar = true;
                starIdentity = candidate;
                continue;
            }

            if (!starIdentity.Equals(candidate))
            {
                ambiguous = true;
                return false;
            }
        }

        if (!foundStar)
            return false;

        identity = starIdentity;
        return true;
    }

    private static string GetDependencyCacheKey(string resolvedId, string? importType)
    {
        return string.IsNullOrEmpty(importType)
            ? resolvedId
            : resolvedId + "\u0000" + importType;
    }

    private static HashSet<string>? ComputeAmbiguousStarExportNames(
        JsRealm realm,
        JsPlainObject importsObject,
        IReadOnlyDictionary<string, string> explicitLocalExports,
        IReadOnlyList<ExportFromBindingResolved> exportFromBindings,
        IReadOnlyList<ExportNamespaceFromBindingResolved> exportNamespaceFromBindings,
        IReadOnlyList<string> exportStars)
    {
        if (exportStars.Count == 0)
            return null;

        var explicitNames = new HashSet<string>(explicitLocalExports.Keys, StringComparer.Ordinal);
        for (var i = 0; i < exportFromBindings.Count; i++)
            explicitNames.Add(exportFromBindings[i].ExportedName);
        for (var i = 0; i < exportNamespaceFromBindings.Count; i++)
            explicitNames.Add(exportNamespaceFromBindings[i].ExportedName);

        var seen = new Dictionary<string, StarExportCandidate>(StringComparer.Ordinal);
        HashSet<string>? ambiguous = null;
        for (var i = 0; i < exportStars.Count; i++)
        {
            if (!importsObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck(exportStars[i]), out var depNsValue,
                    out _))
                continue;
            if (!depNsValue.TryGetObject(out var depNs))
                continue;

            foreach (var entry in depNs.Shape.EnumerateSlotInfos())
            {
                var atom = entry.Key;
                if (atom < 0)
                    continue;
                var flags = entry.Value.Flags;
                if ((flags & JsShapePropertyFlags.Enumerable) == 0)
                    continue;

                var name = realm.Atoms.AtomToString(atom);
                if (string.Equals(name, "default", StringComparison.Ordinal))
                    continue;
                if (explicitNames.Contains(name))
                    continue;
                if (!TryResolveExportBindingIdentity(realm, depNs, name, out var identity))
                    identity = ExportBindingIdentity.Unknown(exportStars[i], name);

                if (!seen.TryGetValue(name, out var existing))
                {
                    seen.Add(name, new(identity));
                    continue;
                }

                if (existing.Identity.Equals(identity))
                    continue;

                (ambiguous ??= new(StringComparer.Ordinal)).Add(name);
            }
        }

        return ambiguous;
    }

    private static bool TryResolveExportBindingIdentity(JsRealm realm, JsObject namespaceObject, string exportName,
        out ExportBindingIdentity identity)
    {
        identity = default;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return TryResolveExportBindingIdentityCore(realm, namespaceObject, exportName, visited, out identity);
    }

    private static bool TryResolveExportBindingIdentityCore(
        JsRealm realm,
        JsObject namespaceObject,
        string exportName,
        HashSet<string> visited,
        out ExportBindingIdentity identity)
    {
        identity = default;
        var visitKey = $"{RuntimeHelpers.GetHashCode(namespaceObject)}:{exportName}";
        if (!visited.Add(visitKey))
            return false;

        if (TryGetArrayIndexFromCanonicalString(exportName, out var idx))
        {
            if (!namespaceObject.TryGetOwnElementDescriptor(idx, out _))
                return false;
            identity = ExportBindingIdentity.Unknown(visitKey, exportName);
            return true;
        }

        var atom = realm.Atoms.InternNoCheck(exportName);
        if (!namespaceObject.TryGetOwnPropertySlotInfoAtom(atom, out var ownSlot))
            return false;
        if ((ownSlot.Flags & JsShapePropertyFlags.HasGetter) == 0)
        {
            identity = ExportBindingIdentity.Unknown(visitKey, exportName);
            return true;
        }

        var getterValue = namespaceObject.Slots[ownSlot.Slot];
        if (!getterValue.TryGetObject(out var getterObj) || getterObj is not JsHostFunction getter)
            return false;

        var userData = getter.UserData;
        switch (userData)
        {
            case LocalExportSlotGetterCapture local:
            {
                if (local.CellIndex > 0)
                {
                    identity = ExportBindingIdentity.Named(local.ModuleResolvedId, local.ExportedName);
                    return true;
                }

                var importIndex = -local.CellIndex - 1;
                if ((uint)importIndex >= (uint)local.Bindings.RegularImports.Length)
                    return false;
                var slot = local.Bindings.RegularImports[importIndex];
                if (slot.Kind == ModuleVariableSlotKind.NamespaceImport &&
                    slot.ResolvedDependencyId is not null)
                {
                    identity = ExportBindingIdentity.Namespace(slot.ResolvedDependencyId, slot.ImportType);
                    return true;
                }

                if (slot.Kind == ModuleVariableSlotKind.NamedImport &&
                    slot.ResolvedDependencyId is not null &&
                    slot.ImportedName is not null)
                {
                    identity = ExportBindingIdentity.Named(slot.ResolvedDependencyId, slot.ImportedName);
                    return true;
                }

                return false;
            }
            case ExportFromGetterCapture from:
                return TryResolveExportBindingIdentityCore(realm, from.Dependency, from.ImportedName, visited,
                    out identity);
            case ExportStarGetterCapture star:
            {
                var starName = realm.Atoms.AtomToString(star.Atom);
                return TryResolveExportBindingIdentityCore(realm, star.Dependency, starName, visited, out identity);
            }
            case NamespaceExportGetterCapture nsCapture:
                if (nsCapture.NamespaceValue.TryGetObject(out var nsObj))
                {
                    identity = ExportBindingIdentity.NamespaceObject(nsObj);
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryGetArrayIndexFromCanonicalString(string text, out uint index)
    {
        index = 0;
        if (string.IsNullOrEmpty(text))
            return false;
        if (!uint.TryParse(text, NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsed))
            return false;
        if (parsed == uint.MaxValue)
            return false;
        if (!string.Equals(parsed.ToString(CultureInfo.InvariantCulture), text,
                StringComparison.Ordinal))
            return false;
        index = parsed;
        return true;
    }

    private static JsHostFunction CreateSetFunctionNameHelper(JsRealm realm)
    {
        return new(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var callee = info.Function;
            if (args.Length < 2)
                return JsValue.Undefined;
            if (!args[0].TryGetObject(out var targetObj) || targetObj is not JsFunction targetFn)
                return JsValue.Undefined;
            if (!args[1].IsString)
                return JsValue.Undefined;

            if (!ShouldSetModuleDefaultExportName(innerRealm, targetFn))
                return JsValue.Undefined;
            const int nameAtom = IdName;
            targetFn.DefineDataPropertyAtom(innerRealm, nameAtom, JsValue.FromString(args[1].AsString()),
                JsShapePropertyFlags.Configurable);
            return JsValue.Undefined;
        }, "moduleSetFunctionName", 2);
    }

    private static bool ShouldSetModuleDefaultExportName(JsRealm realm, JsFunction targetFn)
    {
        const int nameAtom = IdName;
        if (!targetFn.TryGetOwnPropertySlotInfoAtom(nameAtom, out var ownSlot))
            return true;

        var ownValue = targetFn.Slots[ownSlot.Slot];
        return ownValue.IsString && ownValue.AsString().Length == 0;
    }

    private static HashSet<string>? CollectDefaultNameEligibleExportLocals(
        IReadOnlyList<ModuleExecutionOp> operations)
    {
        HashSet<string>? result = null;
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op.Kind != ModuleExecutionOpKind.ExportDefaultExpression ||
                !op.SetDefaultName ||
                string.IsNullOrEmpty(op.ExportLocalName))
                continue;

            result ??= new(StringComparer.Ordinal);
            result.Add(op.ExportLocalName!);
        }

        return result;
    }


    private void PushModuleRuntimeBindings(ModuleExecutionBindings bindings)
    {
        lock (moduleRuntimeBindingsGate)
        {
            moduleRuntimeBindings.Push(bindings);
        }
    }

    private void PopModuleRuntimeBindings()
    {
        lock (moduleRuntimeBindingsGate)
        {
            if (moduleRuntimeBindings.Count == 0)
                throw new InvalidOperationException("Module runtime bindings stack underflow.");
            moduleRuntimeBindings.Pop();
        }
    }

    internal JsValue GetCurrentModuleImportsBinding()
    {
        return GetCurrentModuleRuntimeBindings().Imports;
    }

    internal JsValue GetCurrentModuleDefineLiveExportBinding()
    {
        return GetCurrentModuleRuntimeBindings().DefineLiveExport;
    }

    internal JsValue GetCurrentModuleSetFunctionNameBinding()
    {
        return GetCurrentModuleRuntimeBindings().SetFunctionName;
    }

    internal JsValue LoadCurrentModuleVariable(JsRealm realm, int cellIndex, int depth)
    {
        var bindings = GetModuleBindingsFromContextDepth(realm, depth);
        return LoadModuleVariableFromBindings(realm, bindings, cellIndex);
    }

    internal void StoreCurrentModuleVariable(JsRealm realm, int cellIndex, int depth, JsValue value)
    {
        var bindings = GetModuleBindingsFromContextDepth(realm, depth);
        if (cellIndex <= 0)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Unsupported module import store",
                "MODULE_IMPORT_STORE_UNSUPPORTED");

        var exportIndex = cellIndex - 1;
        if ((uint)exportIndex >= (uint)bindings.RegularExports.Length)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"Module export cell index out of range: {cellIndex}", "MODULE_SLOT_OOB");

        var cell = bindings.RegularExports[exportIndex];
        cell.LocalValue = value;
    }

    private static ModuleExecutionBindings GetModuleBindingsFromContextDepth(JsRealm realm, int depth)
    {
        var moduleContext = realm.GetContextAtDepth(depth);
        if (moduleContext.ModuleBindings is null)
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Module runtime binding requested without active module context",
                "MODULE_RUNTIME_BINDING_NO_CONTEXT");
        return moduleContext.ModuleBindings;
    }

    internal JsValue GetCurrentModuleImportMetaBinding(JsRealm realm, JsContext? context = null)
    {
        var bindings = context?.ModuleBindings ?? GetCurrentModuleRuntimeBindings();
        var imports = bindings.Imports;
        if (imports.TryGetObject(out var importsObj) &&
            importsObj is JsObject importsOkojo &&
            importsOkojo.TryGetPropertyAtom(realm, IdOkojoMeta, out var importMeta, out _))
            return importMeta;

        return JsValue.Undefined;
    }

    internal bool TryGetCurrentModuleRuntimeBindings(out ModuleExecutionBindings bindings)
    {
        lock (moduleRuntimeBindingsGate)
        {
            if (moduleRuntimeBindings.Count == 0)
            {
                bindings = null!;
                return false;
            }

            bindings = moduleRuntimeBindings.Peek();
            return true;
        }
    }

    internal Okojo.Objects.JsDisposableStackObject GetCurrentModuleExplicitResourceStack()
    {
        var bindings = GetCurrentModuleRuntimeBindings();
        if (bindings.ExplicitResourceStack is null)
        {
            throw new JsRuntimeException(
                JsErrorKind.InternalError,
                "Current module does not have an explicit resource stack");
        }

        return bindings.ExplicitResourceStack;
    }

    internal bool TryGetCurrentModuleResolvedId(out string resolvedId)
    {
        if (TryGetCurrentModuleRuntimeBindings(out var bindings))
        {
            resolvedId = bindings.ModuleResolvedId;
            return true;
        }

        resolvedId = string.Empty;
        return false;
    }

    private ModuleExecutionBindings GetCurrentModuleRuntimeBindings()
    {
        lock (moduleRuntimeBindingsGate)
        {
            if (moduleRuntimeBindings.Count == 0)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Module runtime binding requested outside active module execution",
                    "MODULE_RUNTIME_BINDING_NO_CONTEXT");
            return moduleRuntimeBindings.Peek();
        }
    }

    private readonly struct ExportBindingIdentity : IEquatable<ExportBindingIdentity>
    {
        private readonly byte kind; // 0: unknown, 1: named, 2: namespace, 3: namespace-object
        private readonly string? key1;
        private readonly string? key2;
        private readonly int objectId;

        private ExportBindingIdentity(byte kind, string? key1, string? key2, int objectId)
        {
            this.kind = kind;
            this.key1 = key1;
            this.key2 = key2;
            this.objectId = objectId;
        }

        public static ExportBindingIdentity Named(string moduleResolvedId, string bindingName)
        {
            return new(1, moduleResolvedId, bindingName, 0);
        }

        public static ExportBindingIdentity Namespace(string moduleResolvedId, string? importType = null)
        {
            return new(2, GetDependencyCacheKey(moduleResolvedId, importType), null, 0);
        }

        public static ExportBindingIdentity NamespaceObject(JsObject nsObject)
        {
            return new(3, null, null, RuntimeHelpers.GetHashCode(nsObject));
        }

        public static ExportBindingIdentity Unknown(string sourceKey, string exportName)
        {
            return new(0, sourceKey, exportName, 0);
        }

        public bool Equals(ExportBindingIdentity other)
        {
            return kind == other.kind &&
                   objectId == other.objectId &&
                   string.Equals(key1, other.key1, StringComparison.Ordinal) &&
                   string.Equals(key2, other.key2, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is ExportBindingIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(kind, objectId, key1, key2);
        }
    }

    private readonly struct StarExportCandidate(ExportBindingIdentity identity)
    {
        public ExportBindingIdentity Identity { get; } = identity;
    }
}
