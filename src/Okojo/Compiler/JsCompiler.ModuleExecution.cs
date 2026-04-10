using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler;

internal readonly record struct ModuleExecutionEnvironment(
    int SlotCount,
    JsValue[] InitialSlotValues);

public sealed partial class JsCompiler
{
    internal JsScript CompileModuleExecution(
        ModuleExecutionPlan executionPlan,
        string? moduleSourceText = null,
        string? moduleSourcePath = null,
        JsIdentifierTable? moduleIdentifierTable = null)
    {
        strictDeclared = true;
        sourceCode = new(moduleSourceText, moduleSourcePath);
        identifierTable = moduleIdentifierTable ?? identifierTable;
        builder.SetSourceText(moduleSourceText);
        builder.SetStrictDeclared(true);
        var script = CompileModuleExecutionOpsCore(executionPlan);
        script.BindAgent(Vm.Agent);
        return script;
    }

    internal JsScript CompileModuleExecutionAsync(ModuleExecutionPlan executionPlan,
        string? moduleSourceText = null,
        string? moduleSourcePath = null,
        JsIdentifierTable? moduleIdentifierTable = null)
    {
        strictDeclared = true;
        sourceCode = new(moduleSourceText, moduleSourcePath);
        identifierTable = moduleIdentifierTable ?? identifierTable;
        builder.SetSourceText(moduleSourceText);
        builder.SetStrictDeclared(true);

        var asyncModuleFunction = CompileModuleExecutionFunction(executionPlan, true);
        var funcIdx = builder.AddObjectConstant(asyncModuleFunction);
        EmitCreateClosureByIndex(funcIdx);
        var funcReg = AllocateSyntheticLocal("$module_async_fn");
        EmitStarRegister(funcReg);
        EmitCallUndefinedReceiver(funcReg, 0, 0);
        EmitRaw(JsOpCode.Return);
        var script = builder.ToScript() with { SourcePath = CurrentSourcePath, SourceCode = sourceCode };
        script.BindAgent(Vm.Agent);
        return script;
    }

    private JsBytecodeFunction CompileModuleExecutionFunction(ModuleExecutionPlan executionPlan, bool isAsync)
    {
        using var funcCompiler = new JsCompiler(
            this,
            isAsync ? JsBytecodeFunctionKind.Async : JsBytecodeFunctionKind.Normal,
            forceModuleFunctionContext: forceModuleFunctionContext);

        var script = funcCompiler.CompileModuleExecutionOpsCore(executionPlan);
        return new(
            Vm,
            script,
            "",
            funcCompiler.requiresClosureBinding,
            hasNewTarget: funcCompiler.compiledFunctionHasNewTarget,
            kind: GetFunctionKind(false, isAsync),
            isArrow: false,
            formalParameterCount: 0,
            hasSimpleParameterList: true,
            isClassConstructor: false,
            isDerivedConstructor: false,
            expectedArgumentCount: 0);
    }

    internal JsBytecodeFunction CompileHoistedFunctionTemplate(
        JsFunctionDeclaration declaration,
        string? functionSourceText = null,
        string? functionSourcePath = null,
        JsIdentifierTable? functionIdentifierTable = null)
    {
        var nextSourceText = functionSourceText ?? CurrentSourceText;
        var nextSourcePath = functionSourcePath ?? CurrentSourcePath;
        sourceCode = ReferenceEquals(nextSourceText, CurrentSourceText) &&
                     string.Equals(nextSourcePath, CurrentSourcePath, StringComparison.Ordinal)
            ? sourceCode
            : new(nextSourceText, nextSourcePath);
        identifierTable = functionIdentifierTable ?? identifierTable;
        builder.SetSourceText(nextSourceText);

        var parameterPlan = FunctionParameterPlan.FromFunction(declaration);
        return CompileFunctionObject(
            declaration.Name,
            parameterPlan,
            declaration.Body,
            CreateFunctionShape(declaration.IsGenerator, declaration.IsAsync),
            sourceStartPosition: declaration.Position);
    }

    internal JsBytecodeFunction CompileHoistedFunctionTemplate(
        JsFunctionExpression expression,
        string compiledName,
        string? functionSourceText = null,
        string? functionSourcePath = null,
        JsIdentifierTable? functionIdentifierTable = null)
    {
        var nextSourceText = functionSourceText ?? CurrentSourceText;
        var nextSourcePath = functionSourcePath ?? CurrentSourcePath;
        sourceCode = ReferenceEquals(nextSourceText, CurrentSourceText) &&
                     string.Equals(nextSourcePath, CurrentSourcePath, StringComparison.Ordinal)
            ? sourceCode
            : new(nextSourceText, nextSourcePath);
        identifierTable = functionIdentifierTable ?? identifierTable;
        builder.SetSourceText(nextSourceText);

        var parameterPlan = FunctionParameterPlan.FromFunction(expression);
        return CompileFunctionObject(
            compiledName,
            parameterPlan,
            expression.Body,
            CreateFunctionShape(expression.IsGenerator, expression.IsAsync),
            sourceStartPosition: expression.Position);
    }

    private JsScript CompileModuleExecutionOpsCore(ModuleExecutionPlan executionPlan)
    {
        cachedNewTargetRegister = -1;
        compiledFunctionHasNewTarget = false;
        nextGeneratorSuspendId = 0;
        generatorSwitchInstructionPc = -1;
        generatorResumeTargetPcBySuspendId.Clear();
        generatorResumeValueTempRegister = -1;
        generatorResumeModeTempRegister = -1;
        currentFunctionParameterPlan = null;
        ClearParameterBindingFlags();
        functionHasParameterExpressions = false;
        emittingParameterInitializers = false;
        hasEmittedDeferredInstanceInitializers = false;

        if (functionKind != JsBytecodeFunctionKind.Normal)
        {
            generatorSwitchInstructionPc = builder.CodeLength;
            EmitRaw(JsOpCode.SwitchOnGeneratorState, 0xFF, 0, 0);
        }

        var executeStatements = PrepareModuleExecutionEnvironmentCore(executionPlan);
        try
        {
            EmitFunctionContextPrologueIfNeeded();

            foreach (var statement in executeStatements)
                if (statement is JsFunctionDeclaration decl &&
                    !ShouldDeferModuleFunctionHoistToInstantiation(decl))
                    HoistFunction(decl);
        }
        finally
        {
            executeStatements.Clear();
            Vm.ReturnCompileList(executeStatements);
        }

        if (requiresArgumentsObject)
        {
            var argumentsReg = EnsureSyntheticArgumentsRegister();
            EmitRaw(JsOpCode.CreateMappedArguments);
            EmitStarRegister(argumentsReg);
        }

        EmitRegisterLocalsPrologueInitialization();

        var alwaysReturns = false;
        enableAliasScopeRegisterAllocation = true;
        try
        {
            for (var i = 0; i < executionPlan.Operations.Count; i++)
            {
                var op = executionPlan.Operations[i];
                switch (op.Kind)
                {
                    case ModuleExecutionOpKind.ExecuteStatement:
                        if (op.Statement is null)
                            throw new InvalidOperationException("Module execution op missing statement.");
                        VisitStatement(op.Statement);
                        if (StatementAlwaysReturns(op.Statement) || StatementNeverCompletesNormally(op.Statement))
                            alwaysReturns = true;
                        break;
                    case ModuleExecutionOpKind.ExportDefaultExpression:
                        if (op.Expression is null)
                            throw new InvalidOperationException(
                                "Module execution op missing default-export expression.");
                        if (string.IsNullOrEmpty(op.ExportLocalName))
                            throw new InvalidOperationException(
                                "Module execution default-export op missing local export name.");
                        EmitExportDefaultOperation(op.Expression, op.ExportLocalName, op.SetDefaultName);
                        break;
                    case ModuleExecutionOpKind.InitializeHoistedDefaultExport:
                        EmitLdaTheHole();
                        break;
                    default:
                        throw new InvalidOperationException("Unknown module execution op kind.");
                }

                if (alwaysReturns)
                    break;
            }
        }
        finally
        {
            enableAliasScopeRegisterAllocation = false;
        }

        EmitModuleLocalLiveExportEpilogue();
        if (!alwaysReturns)
            EmitRaw(JsOpCode.Return);

        if (functionKind != JsBytecodeFunctionKind.Normal)
            PatchGeneratorSwitchTable();

        return builder.ToScript() with { SourcePath = CurrentSourcePath, SourceCode = sourceCode };
    }

    internal ModuleExecutionEnvironment DescribeModuleExecutionEnvironment(
        ModuleExecutionPlan executionPlan,
        JsIdentifierTable? moduleIdentifierTable = null)
    {
        identifierTable = moduleIdentifierTable ?? identifierTable;
        var executeStatements = PrepareModuleExecutionEnvironmentCore(executionPlan);
        try
        {
            return new(
                currentContextSlotById.Count,
                GetCurrentContextInitialValues());
        }
        finally
        {
            executeStatements.Clear();
            Vm.ReturnCompileList(executeStatements);
        }
    }

    private bool ShouldDeferModuleFunctionHoistToInstantiation(JsFunctionDeclaration declaration)
    {
        return moduleVariableBindings is not null && moduleVariableBindings.ContainsKey(declaration.Name);
    }

    private List<JsStatement> PrepareModuleExecutionEnvironmentCore(ModuleExecutionPlan executionPlan)
    {
        var executeStatements = Vm.RentCompileList<JsStatement>(executionPlan.Operations.Count);
        for (var i = 0; i < executionPlan.Operations.Count; i++)
        {
            var op = executionPlan.Operations[i];
            if (op.Kind == ModuleExecutionOpKind.ExecuteStatement && op.Statement is not null)
                executeStatements.Add(op.Statement);
        }

        PredeclareLocals(executeStatements, false);
        if (requiresArgumentsObject)
            EnsureSyntheticArgumentsRegister();
        ClearCapturedByChildFlags();
        PrecomputeDirectChildCaptures(executeStatements);
        for (var i = 0; i < executionPlan.Operations.Count; i++)
        {
            var op = executionPlan.Operations[i];
            switch (op.Kind)
            {
                case ModuleExecutionOpKind.ExportDefaultExpression
                    when op.Expression is not null:
                case ModuleExecutionOpKind.InitializeHoistedDefaultExport
                    when op.Expression is not null:
                    ScanForDirectNestedFunctionCapturesInExpression(op.Expression);
                    break;
            }
        }

        AssignCurrentContextSlots();
        ComputeSafeLexicalRegisterPrologueHoleInitSkips(executeStatements);
        return executeStatements;
    }

    private JsValue[] GetCurrentContextInitialValues()
    {
        if (currentContextSlotById.Count == 0)
            return [];

        var initialValues = new JsValue[currentContextSlotById.Count];
        foreach (var kvp in currentContextSlotById.OrderBy(static kvp => kvp.Value))
        {
            JsValue initialValue;
            if (kvp.Key == SyntheticArgumentsSymbolId)
                initialValue = JsValue.Undefined;
            else if (kvp.Key == DerivedThisSymbolId || IsLexicalLocalBinding(kvp.Key))
                initialValue = JsValue.TheHole;
            else
                initialValue = JsValue.Undefined;

            initialValues[kvp.Value] = initialValue;
        }

        return initialValues;
    }

    private void EmitExportDefaultOperation(JsExpression expression, string exportLocalName, bool setDefaultName)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            VisitExpressionWithInferredName(expression, setDefaultName ? "default" : null);
            var valueReg = AllocateTemporaryRegister();
            EmitStarRegister(valueReg);

            EmitLdaRegister(valueReg);
            StoreIdentifier(exportLocalName, true, "default");

            if (setDefaultName && ShouldAssignAnonymousFunctionName(expression))
            {
                // __js_current_module_set_function_name(<default export value>, "default")
                var argStart = AllocateTemporaryRegisterBlock(2);
                EmitMoveRegister(valueReg, argStart);
                var defaultIdx = builder.AddObjectConstant("default");
                EmitLdaStringConstantByIndex(defaultIdx);
                EmitStarRegister(argStart + 1);
                EmitCallRuntime(RuntimeId.GetCurrentModuleSetFunctionName, 0, 0);
                var calleeReg = AllocateTemporaryRegister();
                EmitStarRegister(calleeReg);
                EmitCallUndefinedReceiver(calleeReg, argStart, 2);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }
}
