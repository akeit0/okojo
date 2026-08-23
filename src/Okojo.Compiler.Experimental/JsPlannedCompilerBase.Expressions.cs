using System.Globalization;
using System.Numerics;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;
using Okojo.JavaScript.Values;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    private void EmitExpression(FlatAst ast, int nodeIndex) =>
        EmitExpression(ast, nodeIndex, ExpressionResult.Value);

    private void EmitExpressionForEffect(FlatAst ast, int nodeIndex) =>
        EmitExpression(ast, nodeIndex, ExpressionResult.Effect);

    private void EmitExpressionForTest(
        FlatAst ast,
        int nodeIndex,
        BytecodeBuilder.Label target,
        bool jumpIfTrue
    ) => EmitExpression(ast, nodeIndex, ExpressionResult.Test(target, jumpIfTrue));

    private void EmitExpression(FlatAst ast, int nodeIndex, ExpressionResult result)
    {
        ref readonly var node = ref ast[nodeIndex];
        if (node.Kind == AstKind.NewTargetExpression)
            hasNewTarget = true;
        if (result.Mode == ExpressionResultMode.Test)
        {
            EmitTestExpression(ast, nodeIndex, node, result.Target, result.JumpIfTrue);
            return;
        }

        if (
            result.Mode == ExpressionResultMode.Effect
            && node.Kind
                is AstKind.NullLiteral
                    or AstKind.BooleanLiteral
                    or AstKind.NumericLiteral
                    or AstKind.BigIntLiteral
                    or AstKind.StringLiteral
                    or AstKind.NewTargetExpression
        )
            return;

        switch (node.Kind)
        {
            case AstKind.NullLiteral:
                builder.EmitLda(JsOpCode.LdaNull);
                return;
            case AstKind.BooleanLiteral:
                builder.EmitLda(node.Arg0 != 0 ? JsOpCode.LdaTrue : JsOpCode.LdaFalse);
                return;
            case AstKind.NumericLiteral:
                EmitNumericLiteral(ast.GetNumber(node.Arg0));
                return;
            case AstKind.BigIntLiteral:
                EmitTypedConstant(
                    Tag.JsTagBigInt,
                    builder.AddObjectConstant(
                        new JsBigInt(
                            BigInteger.Parse(
                                ast.GetString(node.Arg0),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture
                            )
                        )
                    )
                );
                return;
            case AstKind.StringLiteral:
                EmitStringLiteral(ast.GetString(node.Arg0));
                return;
            case AstKind.RegExpLiteral:
                EmitRegExpLiteral(ast.GetString(node.Arg0), ast.GetString(node.Arg1));
                return;
            case AstKind.Identifier:
                EmitIdentifierLoad(ast.GetString(node.Arg0));
                return;
            case AstKind.PrivateIdentifier:
                throw new InvalidOperationException(
                    "A private identifier is only valid as the left side of 'in'."
                );
            case AstKind.ThisExpression:
                builder.EmitLda(JsOpCode.LdaThis);
                return;
            case AstKind.NewTargetExpression:
                builder.EmitLda(
                    emittingInstanceFieldInitializer ? JsOpCode.LdaUndefined : JsOpCode.LdaNewTarget
                );
                return;
            case AstKind.ImportMetaExpression:
                builder.EmitCallRuntime((int)RuntimeId.GetCurrentModuleImportMeta, 0, 0);
                return;
            case AstKind.ImportCallExpression:
                EmitImportCallExpression(ast, node);
                return;
            case AstKind.SuperExpression:
                throw new InvalidOperationException("Bare super cannot be emitted as a value.");
            case AstKind.AssignmentExpression
                when (JsAssignmentOperator)node.Arg2 == JsAssignmentOperator.Assign
                    && ast[node.Arg0].Kind is AstKind.ArrayExpression or AstKind.ObjectExpression:
                EmitDestructuringAssignment(ast, node);
                return;
            case AstKind.AssignmentExpression when ast[node.Arg0].Kind == AstKind.Identifier:
                EmitIdentifierAssignment(
                    ast,
                    ast.GetString(ast[node.Arg0].Arg0),
                    (JsAssignmentOperator)node.Arg2,
                    node.Arg1,
                    ast.GetPosition(nodeIndex) == ast.GetPosition(node.Arg0)
                );
                return;
            case AstKind.AssignmentExpression when ast[node.Arg0].Kind == AstKind.MemberExpression:
                EmitMemberAssignment(
                    ast,
                    ast[node.Arg0],
                    (JsAssignmentOperator)node.Arg2,
                    node.Arg1
                );
                return;
            case AstKind.AssignmentExpression:
                throw new NotSupportedException(
                    $"{CompilerName} does not support this assignment target."
                );
            case AstKind.BinaryExpression:
                EmitBinaryExpression(ast, node, result);
                return;
            case AstKind.UnaryExpression:
                EmitUnaryExpression(ast, node);
                return;
            case AstKind.UpdateExpression:
                EmitUpdateExpression(ast, node);
                return;
            case AstKind.ConditionalExpression:
                EmitConditionalExpression(ast, node, result);
                return;
            case AstKind.SequenceExpression:
                EmitSequenceExpression(ast, node, result);
                return;
            case AstKind.CallExpression:
                EmitCallExpression(ast, node, optional: false);
                return;
            case AstKind.OptionalCallExpression:
                EmitCallExpression(ast, node, optional: true);
                return;
            case AstKind.NewExpression:
                EmitNewExpression(ast, node);
                return;
            case AstKind.MemberExpression:
                EmitMemberExpression(ast, node);
                return;
            case AstKind.OptionalChainExpression:
                EmitOptionalChainExpression(ast, node.Arg0);
                return;
            case AstKind.ArrayExpression:
                EmitArrayExpression(ast, node);
                return;
            case AstKind.ObjectExpression:
                EmitObjectExpression(ast, node);
                return;
            case AstKind.TemplateExpression:
                EmitTemplateExpression(ast, node);
                return;
            case AstKind.TaggedTemplateExpression:
                EmitTaggedTemplateExpression(ast, node);
                return;
            case AstKind.YieldExpression:
                EmitYieldExpression(ast, node);
                return;
            case AstKind.AwaitExpression:
                EmitAwaitExpression(ast, node);
                return;
            case AstKind.FunctionExpression:
            case AstKind.ArrowFunctionExpression:
                EmitFunctionExpression(ast, node.Arg0, node.Arg1);
                return;
            case AstKind.ClassExpression:
                EmitClassExpression(ast, node.Arg0);
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support flat expression '{node.Kind}'."
                );
        }
    }

    private void EmitTestExpression(
        FlatAst ast,
        int nodeIndex,
        AstNode node,
        BytecodeBuilder.Label target,
        bool jumpIfTrue
    )
    {
        if (node.Kind == AstKind.BooleanLiteral)
        {
            if ((node.Arg0 != 0) == jumpIfTrue)
                EmitJump(target);
            return;
        }

        if (
            node.Kind == AstKind.UnaryExpression
            && (JsUnaryOperator)node.Arg1 == JsUnaryOperator.LogicalNot
        )
        {
            EmitExpressionForTest(ast, node.Arg0, target, !jumpIfTrue);
            return;
        }

        if (node.Kind == AstKind.BinaryExpression)
        {
            var op = (JsBinaryOperator)node.Arg2;
            if (op is JsBinaryOperator.LogicalAnd or JsBinaryOperator.LogicalOr)
            {
                EmitLogicalTestExpression(ast, node, op, target, jumpIfTrue);
                return;
            }
        }

        if (node.Kind == AstKind.ConditionalExpression)
        {
            var alternate = builder.CreateLabel();
            var end = builder.CreateLabel();
            EmitExpressionForTest(ast, node.Arg0, alternate, jumpIfTrue: false);
            EmitExpressionForTest(ast, node.Arg1, target, jumpIfTrue);
            EmitJump(end);
            builder.BindLabel(alternate);
            EmitExpressionForTest(ast, node.Arg2, target, jumpIfTrue);
            builder.BindLabel(end);
            return;
        }

        if (node.Kind == AstKind.SequenceExpression)
        {
            var expressions = ast.ChildRange(node.Arg0, node.Arg1);
            if (expressions.Length == 0)
            {
                if (!jumpIfTrue)
                    EmitJump(target);
                return;
            }

            for (var i = 0; i < expressions.Length - 1; i++)
                EmitExpressionForEffect(ast, expressions[i]);
            EmitExpressionForTest(ast, expressions[^1], target, jumpIfTrue);
            return;
        }

        EmitExpression(ast, nodeIndex, ExpressionResult.Value);
        if (jumpIfTrue)
            EmitJumpIfToBooleanTrue(target);
        else
            EmitJumpIfToBooleanFalse(target);
    }

    private void EmitLogicalTestExpression(
        FlatAst ast,
        AstNode node,
        JsBinaryOperator op,
        BytecodeBuilder.Label target,
        bool jumpIfTrue
    )
    {
        if (op == JsBinaryOperator.LogicalAnd)
        {
            if (!jumpIfTrue)
            {
                EmitExpressionForTest(ast, node.Arg0, target, jumpIfTrue: false);
                EmitExpressionForTest(ast, node.Arg1, target, jumpIfTrue: false);
                return;
            }

            var falseFallthrough = builder.CreateLabel();
            EmitExpressionForTest(ast, node.Arg0, falseFallthrough, jumpIfTrue: false);
            EmitExpressionForTest(ast, node.Arg1, target, jumpIfTrue: true);
            builder.BindLabel(falseFallthrough);
            return;
        }

        if (jumpIfTrue)
        {
            EmitExpressionForTest(ast, node.Arg0, target, jumpIfTrue: true);
            EmitExpressionForTest(ast, node.Arg1, target, jumpIfTrue: true);
            return;
        }

        var trueFallthrough = builder.CreateLabel();
        EmitExpressionForTest(ast, node.Arg0, trueFallthrough, jumpIfTrue: true);
        EmitExpressionForTest(ast, node.Arg1, target, jumpIfTrue: false);
        builder.BindLabel(trueFallthrough);
    }

    private void EmitExpressionWithInferredName(FlatAst ast, int nodeIndex, string inferredName)
    {
        ref readonly var node = ref ast[nodeIndex];
        if (node.Kind is AstKind.FunctionExpression or AstKind.ArrowFunctionExpression)
        {
            EmitFunctionExpression(ast, node.Arg0, node.Arg1, inferredName);
            return;
        }
        if (node.Kind == AstKind.ClassExpression)
        {
            EmitClassExpression(ast, node.Arg0, inferredName);
            return;
        }

        EmitExpression(ast, nodeIndex);
    }

    private void EmitExpressionWithComputedName(FlatAst ast, int nodeIndex, int nameRegister)
    {
        ref readonly var node = ref ast[nodeIndex];
        if (node.Kind is AstKind.FunctionExpression or AstKind.ArrowFunctionExpression)
        {
            var function = ast.GetFunction(node.Arg0);
            EmitFunctionExpression(ast, node.Arg0, node.Arg1);
            if (ast.GetString(function.NameStringIndex).Length == 0)
                EmitSetFunctionName(nameRegister);
            return;
        }
        if (node.Kind == AstKind.ClassExpression)
        {
            EmitClassExpression(ast, node.Arg0, inferredNameRegister: nameRegister);
            return;
        }

        EmitExpression(ast, nodeIndex);
    }

    private void EmitSetFunctionName(int nameRegister)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(2);
            EmitStar(arguments);
            EmitLdar(nameRegister);
            EmitStar(arguments + 1);
            builder.EmitCallRuntime((int)RuntimeId.SetFunctionName, arguments, 2);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private bool EmitFunctionExpression(
        FlatAst ast,
        int functionIndex,
        int bodyRoot,
        string? inferredName = null,
        int instanceFieldClassIndex = -1,
        BindingStorage? deferredBinding = null
    )
    {
        var function = ast.GetFunction(functionIndex);
        var hasSelfBinding = ast.GetString(function.NameStringIndex).Length != 0;
        var functionCompiler = new JsPlannedFunctionCompiler(
            Vm,
            BuildChildCaptureBindings(),
            visiblePrivateBindings
        );
        var functionObject = functionCompiler.CompileFunction(
            ast,
            function,
            bodyRoot,
            hasSelfBinding,
            inferredName,
            instanceFieldClassIndex
        );
        if (deferredBinding is { } binding && DeferHoistedFunction(binding, functionObject))
            return true;
        EmitCreateClosureByIndex(builder.AddObjectConstant(functionObject));
        EmitPrivateBrandMappingsForClosure();
        return false;
    }

    private void EmitImportCallExpression(FlatAst ast, in AstNode node)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var count = node.Arg1 < 0 ? 1 : 2;
            var arguments = builder.AllocateTemporaryRegisterBlock(count);
            EmitExpression(ast, node.Arg0);
            EmitStar(arguments);
            if (node.Arg1 >= 0)
            {
                EmitExpression(ast, node.Arg1);
                EmitStar(arguments + 1);
            }
            builder.EmitCallRuntime((int)RuntimeId.DynamicImport, arguments, count);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitRegExpLiteral(string pattern, string flags)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(2);
            EmitStringLiteral(pattern);
            EmitStar(arguments);
            EmitStringLiteral(flags);
            EmitStar(arguments + 1);
            builder.EmitCallRuntime((int)RuntimeId.CreateRegExpLiteral, arguments, 2);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitTemplateExpression(FlatAst ast, in AstNode node)
    {
        var parts = ast.ChildRange(node.Arg0, node.Arg1);
        if (parts.Length < 3 || (parts.Length & 1) == 0)
            throw new InvalidOperationException("Invalid flat template literal layout.");

        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var accumulatorRegister = builder.AllocateTemporaryRegister();
            var hasAccumulator = false;
            var first = ast.GetString(ast[parts[0]].Arg0);
            if (first.Length != 0)
            {
                EmitStringLiteral(first);
                hasAccumulator = true;
            }

            for (var i = 1; i < parts.Length; i += 2)
            {
                if (hasAccumulator)
                    EmitStar(accumulatorRegister);
                EmitExpression(ast, parts[i]);
                builder.Emit(JsOpCode.ToString);
                if (hasAccumulator)
                    EmitRegisterWithSlotOp(JsOpCode.Add, accumulatorRegister);
                hasAccumulator = true;

                var quasi = ast.GetString(ast[parts[i + 1]].Arg0);
                if (quasi.Length == 0)
                    continue;
                EmitStar(accumulatorRegister);
                EmitStringLiteral(quasi);
                EmitRegisterWithSlotOp(JsOpCode.Add, accumulatorRegister);
            }
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitTaggedTemplateExpression(FlatAst ast, in AstNode node)
    {
        var parts = ast.ChildRange(node.Arg1, node.Arg2);
        if (parts.Length < 2 || (parts.Length - 2) % 3 != 0)
            throw new InvalidOperationException("Invalid flat tagged-template layout.");

        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            ref readonly var tag = ref ast[node.Arg0];
            var receiverRegister = -1;
            int functionRegister;
            if (tag.Kind == AstKind.MemberExpression)
            {
                EmitExpression(ast, tag.Arg0);
                receiverRegister = builder.AllocateTemporaryRegister();
                EmitStar(receiverRegister);
                EmitMemberLoad(ast, tag, receiverRegister);
                functionRegister = builder.AllocateTemporaryRegister();
                EmitStar(functionRegister);
            }
            else if (
                tag.Kind == AstKind.OptionalChainExpression
                && ast[tag.Arg0].Kind == AstKind.MemberExpression
            )
            {
                ref readonly var member = ref ast[tag.Arg0];
                var previous = optionalChainNullTarget;
                var nullTarget = builder.CreateLabel();
                var done = builder.CreateLabel();
                optionalChainNullTarget = nullTarget;
                try
                {
                    EmitExpression(ast, member.Arg0);
                    receiverRegister = builder.AllocateTemporaryRegister();
                    EmitStar(receiverRegister);
                    EmitMemberLoad(ast, member, receiverRegister);
                    EmitJump(done);
                    builder.BindLabel(nullTarget);
                    builder.EmitLda(JsOpCode.LdaUndefined);
                    builder.BindLabel(done);
                }
                finally
                {
                    optionalChainNullTarget = previous;
                }
                functionRegister = builder.AllocateTemporaryRegister();
                EmitStar(functionRegister);
            }
            else
            {
                EmitExpression(ast, node.Arg0);
                functionRegister = builder.AllocateTemporaryRegister();
                EmitStar(functionRegister);
            }

            var substitutionCount = (parts.Length - 2) / 3;
            var argumentStart = builder.AllocateTemporaryRegisterBlock(substitutionCount + 1);
            var cooked = new string?[substitutionCount + 1];
            var raw = new string[substitutionCount + 1];
            for (var i = 0; i <= substitutionCount; i++)
            {
                var cookedNode = parts[i * 3];
                cooked[i] = cookedNode < 0 ? null : ast.GetString(ast[cookedNode].Arg0);
                raw[i] = ast.GetString(ast[parts[i * 3 + 1]].Arg0);
            }

            EmitSmi(builder.AddObjectConstant(new JsTemplateSiteDescriptor(cooked, raw)));
            EmitStar(argumentStart);
            builder.EmitCallRuntime((int)RuntimeId.GetTemplateObject, argumentStart, 1);
            EmitStar(argumentStart);
            for (var i = 0; i < substitutionCount; i++)
            {
                EmitExpression(ast, parts[i * 3 + 2]);
                EmitStar(argumentStart + i + 1);
            }

            if (receiverRegister >= 0)
                builder.EmitCallProperty(
                    functionRegister,
                    receiverRegister,
                    argumentStart,
                    substitutionCount + 1
                );
            else
                builder.EmitCallUndefinedReceiver(
                    functionRegister,
                    argumentStart,
                    substitutionCount + 1
                );
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitObjectExpression(FlatAst ast, AstNode node)
    {
        var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
        for (var i = 0; i < properties.Length; i++)
            if (properties[i].IsCoverInitializedName)
                throw new NotSupportedException(
                    "Object shorthand initializers are only valid in destructuring assignments."
                );
        var shape = Vm.EmptyShape;
        var shapePrefixCount = 0;
        for (; shapePrefixCount < properties.Length; shapePrefixCount++)
        {
            ref readonly var property = ref properties[shapePrefixCount];
            if (property.IsComputed || property.IsRest || property.IsAccessor)
                break;
            var name = ast.GetString(property.Key);
            if (AtomTable.TryGetArrayIndexFromCanonicalString(name, out _))
                break;
            var atom = Vm.Atoms.InternNoCheck(name);
            if (shape.TryGetSlotInfo(atom, out _))
                break;
            shape = shape.GetOrAddTransition(atom, JsShapePropertyFlags.Open, out _);
        }

        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            builder.EmitCreateObjectLiteral(builder.AddObjectConstant(shape));
            var objectRegister = builder.AllocateTemporaryRegister();
            EmitStar(objectRegister);
            var keyRegister = -1;
            for (var i = 0; i < properties.Length; i++)
            {
                ref readonly var property = ref properties[i];
                if (i < shapePrefixCount)
                {
                    var name = ast.GetString(property.Key);
                    EmitExpressionWithInferredName(ast, property.ValueNode, name);
                    EmitAttachMethodEnvironmentIfNeeded(ast, property.ValueNode, objectRegister);
                    var atom = Vm.Atoms.InternNoCheck(name);
                    if (!shape.TryGetSlotInfo(atom, out var slotInfo))
                        throw new InvalidOperationException(
                            "Missing precomputed flat object-literal shape slot."
                        );
                    builder.EmitInitializeNamedProperty(objectRegister, slotInfo.Slot);
                    continue;
                }

                if (property.IsRest)
                {
                    EmitObjectLiteralSpread(ast, objectRegister, property.ValueNode);
                    continue;
                }
                if (keyRegister < 0)
                    keyRegister = builder.AllocateTemporaryRegister();
                if (property.IsComputed)
                {
                    EmitExpression(ast, property.Key);
                    EmitStar(keyRegister);
                    builder.EmitCallRuntime((byte)RuntimeId.NormalizePropertyKey, keyRegister, 1);
                }
                else
                    EmitStringLiteral(ast.GetString(property.Key));
                EmitStar(keyRegister);
                if (property.IsAccessor)
                {
                    EmitObjectLiteralAccessor(ast, objectRegister, keyRegister, property);
                    continue;
                }
                if (property.IsComputed)
                    EmitExpression(ast, property.ValueNode);
                else
                    EmitExpressionWithInferredName(
                        ast,
                        property.ValueNode,
                        ast.GetString(property.Key)
                    );
                EmitAttachMethodEnvironmentIfNeeded(ast, property.ValueNode, objectRegister);
                builder.EmitDefineOwnKeyedProperty(objectRegister, keyRegister);
            }
            EmitLdar(objectRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitObjectLiteralAccessor(
        FlatAst ast,
        int objectRegister,
        int keyRegister,
        in FlatObjectProperty property
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(4);
            EmitLdar(objectRegister);
            EmitStar(arguments);
            EmitLdar(keyRegister);
            EmitStar(arguments + 1);
            if (property.IsGetter)
            {
                EmitExpression(ast, property.ValueNode);
                EmitAttachMethodEnvironmentIfNeeded(ast, property.ValueNode, objectRegister);
            }
            else
                builder.EmitLda(JsOpCode.LdaUndefined);
            EmitStar(arguments + 2);
            if (property.IsSetter)
            {
                EmitExpression(ast, property.ValueNode);
                EmitAttachMethodEnvironmentIfNeeded(ast, property.ValueNode, objectRegister);
            }
            else
                builder.EmitLda(JsOpCode.LdaUndefined);
            EmitStar(arguments + 3);
            builder.EmitCallRuntime((int)RuntimeId.DefineObjectAccessor, arguments, 4);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitAttachMethodEnvironmentIfNeeded(
        FlatAst ast,
        int functionNode,
        int homeObjectRegister
    )
    {
        ref readonly var node = ref ast[functionNode];
        if (
            node.Kind is not (AstKind.FunctionExpression or AstKind.ArrowFunctionExpression)
            || !ast.GetFunction(node.Arg0).HasSuperPropertyReference
        )
            return;
        var arguments = builder.AllocateTemporaryRegisterBlock(3);
        EmitStar(arguments);
        EmitLdar(homeObjectRegister);
        EmitStar(arguments + 1);
        builder.EmitLda(JsOpCode.LdaUndefined);
        EmitStar(arguments + 2);
        builder.EmitCallRuntime((int)RuntimeId.SetFunctionMethodEnvironment, arguments, 3);
    }

    private void EmitObjectLiteralSpread(FlatAst ast, int objectRegister, int sourceNode)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(2);
            EmitLdar(objectRegister);
            EmitStar(arguments);
            EmitExpression(ast, sourceNode);
            EmitStar(arguments + 1);
            builder.EmitCallRuntime((int)RuntimeId.CopyDataProperties, arguments, 2);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitArrayExpression(FlatAst ast, AstNode node)
    {
        if ((uint)node.Arg1 > ushort.MaxValue)
            throw new NotSupportedException("Flat array literal exceeds ushort element capacity.");

        var elements = ast.ChildRange(node.Arg0, node.Arg1);
        for (var i = 0; i < elements.Length; i++)
            if (elements[i] >= 0 && ast[elements[i]].Kind == AstKind.SpreadElement)
            {
                EmitArrayExpressionWithSpread(ast, elements);
                return;
            }

        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var literalIndex = builder.AddObjectConstant(node.Arg1);
            builder.EmitCreateArrayLiteral(literalIndex);
            var arrayRegister = builder.AllocateTemporaryRegister();
            EmitStar(arrayRegister);
            for (var i = 0; i < elements.Length; i++)
            {
                if (elements[i] < 0)
                    continue;
                EmitExpression(ast, elements[i]);
                builder.EmitInitializeArrayElement(arrayRegister, i);
            }
            EmitLdar(arrayRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitArrayExpressionWithSpread(FlatAst ast, ReadOnlySpan<int> elements)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            builder.Emit(JsOpCode.CreateEmptyArrayLiteral);
            var arrayRegister = builder.AllocateTemporaryRegister();
            EmitStar(arrayRegister);
            var keyRegister = builder.AllocateTemporaryRegister();
            var indexRegister = builder.AllocateTemporaryRegister();
            builder.EmitLda(JsOpCode.LdaZero);
            EmitStar(indexRegister);

            for (var i = 0; i < elements.Length; i++)
            {
                if (elements[i] >= 0 && ast[elements[i]].Kind == AstKind.SpreadElement)
                {
                    EmitArraySpread(ast, arrayRegister, indexRegister, ast[elements[i]].Arg0);
                    continue;
                }

                EmitLdar(indexRegister);
                EmitStar(keyRegister);
                if (elements[i] < 0)
                    builder.EmitLda(JsOpCode.LdaTheHole);
                else
                    EmitExpression(ast, elements[i]);
                builder.EmitDefineOwnKeyedPropertyNoName(arrayRegister, keyRegister);
                EmitLdar(indexRegister);
                builder.Emit(JsOpCode.Inc);
                EmitStar(indexRegister);
            }

            EmitLdar(arrayRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitArraySpread(FlatAst ast, int arrayRegister, int indexRegister, int sourceNode)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitExpression(ast, sourceNode);
            var sourceRegister = builder.AllocateTemporaryRegister();
            EmitStar(sourceRegister);
            var arguments = builder.AllocateTemporaryRegisterBlock(3);
            EmitLdar(arrayRegister);
            EmitStar(arguments);
            EmitLdar(sourceRegister);
            EmitStar(arguments + 1);
            EmitLdar(indexRegister);
            EmitStar(arguments + 2);
            builder.EmitCallRuntime((int)RuntimeId.AppendArraySpread, arguments, 3);
            EmitStar(indexRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitDestructuringAssignment(FlatAst ast, AstNode assignment)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitExpression(ast, assignment.Arg1);
            var sourceRegister = builder.AllocateTemporaryRegister();
            EmitStar(sourceRegister);
            ref readonly var target = ref ast[assignment.Arg0];
            if (target.Kind == AstKind.ArrayExpression)
                EmitArrayBindingPattern(ast, target, sourceRegister, assignment: true);
            else
                EmitObjectBindingPattern(ast, target, sourceRegister, assignment: true);
            EmitLdar(sourceRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitStoreAssignmentTarget(
        FlatAst ast,
        int targetIndex,
        PreparedMemberReference? preparedTarget
    )
    {
        ref readonly var target = ref ast[targetIndex];
        if (target.Kind == AstKind.Identifier)
        {
            var name = ast.GetString(target.Arg0);
            var hasLocalBinding = TryResolveBindingAccess(
                name,
                out var binding,
                out var contextDepth
            );
            var hasExternalBinding = TryResolveExternalBinding(
                name,
                out var externalBinding,
                out var externalDepth
            );
            EmitResolvedIdentifierStore(
                name,
                hasLocalBinding,
                hasExternalBinding,
                binding,
                contextDepth,
                externalBinding,
                externalDepth
            );
            return;
        }

        if (target.Kind == AstKind.MemberExpression)
        {
            if (preparedTarget is { } prepared)
            {
                EmitPreparedMemberStore(prepared);
                return;
            }

            var marker = builder.GetTemporaryRegisterScopeMarker();
            try
            {
                var valueRegister = builder.AllocateTemporaryRegister();
                EmitStar(valueRegister);
                var reference = PrepareMemberReference(ast, target, normalizeComputedKey: false);
                EmitLdar(valueRegister);
                EmitPreparedMemberStore(reference);
            }
            finally
            {
                builder.ReleaseTemporaryRegistersToMarker(marker);
            }
            return;
        }

        if (target.Kind is not (AstKind.ArrayExpression or AstKind.ObjectExpression))
            throw new NotSupportedException(
                $"{CompilerName} does not support assignment target '{target.Kind}'."
            );

        var nestedMarker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var sourceRegister = builder.AllocateTemporaryRegister();
            EmitStar(sourceRegister);
            if (target.Kind == AstKind.ArrayExpression)
                EmitArrayBindingPattern(ast, target, sourceRegister, assignment: true);
            else
                EmitObjectBindingPattern(ast, target, sourceRegister, assignment: true);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(nestedMarker);
        }
    }

    private void EmitCallExpression(FlatAst ast, AstNode node, bool optional)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            ref readonly var callee = ref ast[node.Arg0];
            if (callee.Kind == AstKind.SuperExpression)
            {
                if (optional)
                    throw new InvalidOperationException("super() cannot be optional.");
                EmitSuperCall(ast, node.Arg1, node.Arg2);
                return;
            }
            if (
                callee.Kind == AstKind.MemberExpression
                && ast[callee.Arg0].Kind == AstKind.SuperExpression
            )
            {
                var reference = PrepareMemberReference(ast, callee, normalizeComputedKey: true);
                EmitPreparedMemberLoad(reference);
                var functionRegister = builder.AllocateTemporaryRegister();
                EmitStar(functionRegister);
                if (HasSpreadArgument(ast, node.Arg1, node.Arg2))
                {
                    EmitSpreadCall(
                        ast,
                        functionRegister,
                        reference.ObjectRegister,
                        node.Arg1,
                        node.Arg2
                    );
                    return;
                }
                var argumentStart = EmitCallArguments(ast, node.Arg1, node.Arg2);
                builder.EmitCallProperty(
                    functionRegister,
                    reference.ObjectRegister,
                    argumentStart,
                    node.Arg2
                );
                return;
            }
            if (callee.Kind == AstKind.MemberExpression)
            {
                EmitExpression(ast, callee.Arg0);
                var objectRegister = builder.AllocateTemporaryRegister();
                EmitStar(objectRegister);
                EmitMemberLoad(ast, callee, objectRegister);
                var functionRegister = builder.AllocateTemporaryRegister();
                EmitStar(functionRegister);
                if (optional)
                    EmitOptionalChainNullCheck(functionRegister);
                if (HasSpreadArgument(ast, node.Arg1, node.Arg2))
                {
                    EmitSpreadCall(ast, functionRegister, objectRegister, node.Arg1, node.Arg2);
                    return;
                }
                var argumentStart = EmitCallArguments(ast, node.Arg1, node.Arg2);
                builder.EmitCallProperty(
                    functionRegister,
                    objectRegister,
                    argumentStart,
                    node.Arg2
                );
                return;
            }

            EmitExpression(ast, node.Arg0);
            var directFunctionRegister = builder.AllocateTemporaryRegister();
            EmitStar(directFunctionRegister);
            if (optional)
                EmitOptionalChainNullCheck(directFunctionRegister);
            if (HasSpreadArgument(ast, node.Arg1, node.Arg2))
            {
                EmitSpreadCall(ast, directFunctionRegister, -1, node.Arg1, node.Arg2);
                return;
            }
            var directArgumentStart = EmitCallArguments(ast, node.Arg1, node.Arg2);
            builder.EmitCallUndefinedReceiver(
                directFunctionRegister,
                directArgumentStart,
                node.Arg2
            );
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitSuperCall(FlatAst ast, int offset, int count)
    {
        if (!HasSpreadArgument(ast, offset, count))
        {
            var argumentStart = EmitCallArguments(ast, offset, count);
            builder.EmitCallRuntime((int)RuntimeId.CallSuperConstructor, argumentStart, count);
            if (InstanceFieldClassIndex >= 0)
                EmitInstanceFieldInitializers(ast, InstanceFieldClassIndex);
            return;
        }

        var arguments = EmitSpreadArguments(ast, offset, count, out var flagsRegister);
        var runtimeArguments = builder.AllocateTemporaryRegisterBlock(count + 1);
        EmitLdar(flagsRegister);
        EmitStar(runtimeArguments);
        for (var i = 0; i < count; i++)
        {
            EmitLdar(arguments + i);
            EmitStar(runtimeArguments + 1 + i);
        }
        builder.EmitCallRuntime(
            (int)RuntimeId.CallSuperConstructorWithSpread,
            runtimeArguments,
            count + 1
        );
        if (InstanceFieldClassIndex >= 0)
            EmitInstanceFieldInitializers(ast, InstanceFieldClassIndex);
    }

    private void EmitNewExpression(FlatAst ast, AstNode node)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitExpression(ast, node.Arg0);
            var functionRegister = builder.AllocateTemporaryRegister();
            EmitStar(functionRegister);
            if (HasSpreadArgument(ast, node.Arg1, node.Arg2))
            {
                EmitSpreadConstruct(ast, functionRegister, node.Arg1, node.Arg2);
                return;
            }
            var argumentStart = EmitCallArguments(ast, node.Arg1, node.Arg2);
            builder.EmitConstruct(functionRegister, argumentStart, node.Arg2);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private static bool HasSpreadArgument(FlatAst ast, int offset, int count)
    {
        var arguments = ast.ChildRange(offset, count);
        for (var i = 0; i < arguments.Length; i++)
            if (ast[arguments[i]].Kind == AstKind.SpreadElement)
                return true;
        return false;
    }

    private int EmitSpreadArguments(FlatAst ast, int offset, int count, out int flagsRegister)
    {
        var start = builder.AllocateTemporaryRegisterBlock(count);
        var flags = new int[count];
        var arguments = ast.ChildRange(offset, count);
        for (var i = 0; i < arguments.Length; i++)
        {
            ref readonly var argument = ref ast[arguments[i]];
            if (argument.Kind == AstKind.SpreadElement)
            {
                flags[i] = 2;
                EmitExpression(ast, argument.Arg0);
                EmitStar(start + i);
                builder.EmitCallRuntime((int)RuntimeId.MaterializeSpreadArgument, start + i, 1);
            }
            else
                EmitExpression(ast, arguments[i]);
            EmitStar(start + i);
        }

        EmitTypedConstant(Tag.JsTagObject, builder.AddObjectConstant(flags));
        flagsRegister = builder.AllocateTemporaryRegister();
        EmitStar(flagsRegister);
        return start;
    }

    private void EmitSpreadCall(
        FlatAst ast,
        int functionRegister,
        int receiverRegister,
        int offset,
        int count
    )
    {
        var argumentStart = EmitSpreadArguments(ast, offset, count, out var flagsRegister);
        var runtimeStart = builder.AllocateTemporaryRegisterBlock(count + 3);
        EmitLdar(functionRegister);
        EmitStar(runtimeStart);
        if (receiverRegister >= 0)
            EmitLdar(receiverRegister);
        else
            builder.EmitLda(JsOpCode.LdaUndefined);
        EmitStar(runtimeStart + 1);
        EmitLdar(flagsRegister);
        EmitStar(runtimeStart + 2);
        for (var i = 0; i < count; i++)
        {
            EmitLdar(argumentStart + i);
            EmitStar(runtimeStart + 3 + i);
        }
        builder.EmitCallRuntime((int)RuntimeId.CallWithSpread, runtimeStart, count + 3);
    }

    private void EmitSpreadConstruct(FlatAst ast, int functionRegister, int offset, int count)
    {
        var argumentStart = EmitSpreadArguments(ast, offset, count, out var flagsRegister);
        var runtimeStart = builder.AllocateTemporaryRegisterBlock(count + 2);
        EmitLdar(functionRegister);
        EmitStar(runtimeStart);
        EmitLdar(flagsRegister);
        EmitStar(runtimeStart + 1);
        for (var i = 0; i < count; i++)
        {
            EmitLdar(argumentStart + i);
            EmitStar(runtimeStart + 2 + i);
        }
        builder.EmitCallRuntime((int)RuntimeId.ConstructWithSpread, runtimeStart, count + 2);
    }

    private int EmitCallArguments(FlatAst ast, int offset, int count)
    {
        if (count == 0)
            return 0;
        var start = builder.AllocateTemporaryRegisterBlock(count);
        var arguments = ast.ChildRange(offset, count);
        for (var i = 0; i < arguments.Length; i++)
        {
            EmitExpression(ast, arguments[i]);
            EmitStar(start + i);
        }
        return start;
    }

    private void EmitMemberExpression(FlatAst ast, AstNode node)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            if (ast[node.Arg0].Kind == AstKind.SuperExpression)
            {
                var reference = PrepareMemberReference(ast, node, normalizeComputedKey: true);
                EmitPreparedMemberLoad(reference);
                return;
            }
            EmitExpression(ast, node.Arg0);
            var objectRegister = builder.AllocateTemporaryRegister();
            EmitStar(objectRegister);
            EmitMemberLoad(ast, node, objectRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitMemberLoad(FlatAst ast, AstNode member, int objectRegister)
    {
        if (((AstMemberFlags)member.Arg2 & AstMemberFlags.OptionalChainLink) != 0)
            EmitOptionalChainNullCheck(objectRegister);
        if (((AstMemberFlags)member.Arg2 & AstMemberFlags.Private) != 0)
        {
            EmitPrivateFieldOp(
                JsOpCode.GetPrivateField,
                objectRegister,
                ResolvePrivateBinding(ast.GetString(member.Arg1))
            );
            return;
        }
        if (((AstMemberFlags)member.Arg2 & AstMemberFlags.Computed) != 0)
        {
            EmitExpression(ast, member.Arg1);
            builder.EmitLdaKeyedProperty(objectRegister);
            return;
        }

        var nameIndex = builder.AddAtomizedStringConstant(ast.GetString(member.Arg1));
        var feedbackSlot = builder.AllocateFeedbackSlot();
        builder.EmitLdaNamedProperty(objectRegister, nameIndex, feedbackSlot);
    }

    private void EmitOptionalChainExpression(FlatAst ast, int expression)
    {
        var previous = optionalChainNullTarget;
        var nullTarget = builder.CreateLabel();
        var done = builder.CreateLabel();
        optionalChainNullTarget = nullTarget;
        try
        {
            EmitExpression(ast, expression);
            EmitJump(done);
            builder.BindLabel(nullTarget);
            builder.EmitLda(JsOpCode.LdaUndefined);
            builder.BindLabel(done);
        }
        finally
        {
            optionalChainNullTarget = previous;
        }
    }

    private void EmitOptionalChainNullCheck(int register)
    {
        if (!optionalChainNullTarget.IsInitialized)
            throw new InvalidOperationException("Optional chain link has no active chain target.");
        EmitLdar(register);
        EmitJumpIfNull(optionalChainNullTarget);
        EmitLdar(register);
        EmitJumpIfUndefined(optionalChainNullTarget);
    }

    private void EmitBinaryExpression(FlatAst ast, AstNode node, ExpressionResult result)
    {
        var op = (JsBinaryOperator)node.Arg2;
        if (op == JsBinaryOperator.In && ast[node.Arg0].Kind == AstKind.PrivateIdentifier)
        {
            var marker = builder.GetTemporaryRegisterScopeMarker();
            try
            {
                EmitExpression(ast, node.Arg1);
                var arguments = builder.AllocateTemporaryRegisterBlock(3);
                EmitStar(arguments);
                var binding = ResolvePrivateBinding(ast.GetString(ast[node.Arg0].Arg0));
                EmitSmi(binding.BrandId);
                EmitStar(arguments + 1);
                EmitSmi(binding.SlotIndex);
                EmitStar(arguments + 2);
                builder.EmitCallRuntime((int)RuntimeId.HasPrivateField, arguments, 3);
            }
            finally
            {
                builder.ReleaseTemporaryRegistersToMarker(marker);
            }
            return;
        }
        if (op is JsBinaryOperator.LogicalAnd or JsBinaryOperator.LogicalOr)
        {
            EmitExpression(ast, node.Arg0);
            var end = builder.CreateLabel();
            if (op == JsBinaryOperator.LogicalAnd)
                EmitJumpIfToBooleanFalse(end);
            else
                EmitJumpIfToBooleanTrue(end);
            EmitExpression(ast, node.Arg1, result);
            builder.BindLabel(end);
            return;
        }

        if (op == JsBinaryOperator.NullishCoalescing)
        {
            EmitExpression(ast, node.Arg0);
            var evaluateRight = builder.CreateLabel();
            var end = builder.CreateLabel();
            EmitJumpIfNull(evaluateRight);
            EmitJumpIfUndefined(evaluateRight);
            EmitJump(end);
            builder.BindLabel(evaluateRight);
            EmitExpression(ast, node.Arg1, result);
            builder.BindLabel(end);
            return;
        }

        EmitExpression(ast, node.Arg0);
        if (
            TryGetSmallIntLiteral(ast, node.Arg1, out var rhsSmi)
            && TryMapSmiBinaryOpcode(op, out var smiOpcode)
        )
        {
            EmitImmediateWithSlotOp(smiOpcode, rhsSmi);
            return;
        }

        var lhsRegister = builder.AllocateTemporaryRegister();
        try
        {
            EmitStar(lhsRegister);
            EmitExpression(ast, node.Arg1);
            if (op == JsBinaryOperator.StrictNotEqual)
            {
                EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, lhsRegister);
                builder.Emit(JsOpCode.LogicalNot);
                return;
            }

            if (!TryMapBinaryOpcode(op, out var opcode))
                throw new NotSupportedException(
                    $"{CompilerName} does not support binary operator '{op}'."
                );
            EmitRegisterWithSlotOp(opcode, lhsRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegister(lhsRegister);
        }
    }

    private void EmitUnaryExpression(FlatAst ast, AstNode node)
    {
        var op = (JsUnaryOperator)node.Arg1;
        if (op == JsUnaryOperator.Delete)
        {
            EmitDeleteExpression(ast, node.Arg0);
            return;
        }

        if (op == JsUnaryOperator.Typeof && ast[node.Arg0].Kind == AstKind.Identifier)
        {
            var name = ast.GetString(ast[node.Arg0].Arg0);
            if (
                !string.Equals(name, "undefined", StringComparison.Ordinal)
                && !TryResolveBinding(name, out _)
                && !TryResolveExternalBinding(name, out _, out _)
            )
            {
                EmitGlobalAccess(name, JsOpCode.TypeOfGlobal, JsOpCode.TypeOfGlobalWide);
                return;
            }
        }

        EmitExpression(ast, node.Arg0);
        switch (op)
        {
            case JsUnaryOperator.Minus:
                builder.Emit(JsOpCode.ToNumeric);
                builder.Emit(JsOpCode.Negate);
                return;
            case JsUnaryOperator.Plus:
                builder.Emit(JsOpCode.ToNumber);
                return;
            case JsUnaryOperator.LogicalNot:
                builder.Emit(JsOpCode.LogicalNot);
                return;
            case JsUnaryOperator.BitwiseNot:
                builder.Emit(JsOpCode.ToNumeric);
                builder.Emit(JsOpCode.BitwiseNot);
                return;
            case JsUnaryOperator.Typeof:
                builder.Emit(JsOpCode.TypeOf);
                return;
            case JsUnaryOperator.Void:
                builder.EmitLda(JsOpCode.LdaUndefined);
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support unary operator '{op}'."
                );
        }
    }

    private void EmitDeleteExpression(FlatAst ast, int argumentIndex)
    {
        ref readonly var argument = ref ast[argumentIndex];
        if (argument.Kind == AstKind.OptionalChainExpression)
        {
            EmitDeleteOptionalChain(ast, argument.Arg0);
            return;
        }
        if (argument.Kind == AstKind.MemberExpression)
        {
            if (ast[argument.Arg0].Kind == AstKind.SuperExpression)
            {
                builder.EmitCallRuntime((int)RuntimeId.ThrowDeleteSuperPropertyReference, 0, 0);
                return;
            }
            var marker = builder.GetTemporaryRegisterScopeMarker();
            try
            {
                EmitExpression(ast, argument.Arg0);
                var registers = builder.AllocateTemporaryRegisterBlock(2);
                EmitStar(registers);
                if (((AstMemberFlags)argument.Arg2 & AstMemberFlags.Computed) != 0)
                    EmitExpression(ast, argument.Arg1);
                else
                    EmitStringLiteral(ast.GetString(argument.Arg1));
                EmitStar(registers + 1);
                builder.EmitCallRuntime(
                    (int)(
                        strictDeclared
                            ? RuntimeId.DeleteKeyedPropertyStrict
                            : RuntimeId.DeleteKeyedProperty
                    ),
                    registers,
                    2
                );
            }
            finally
            {
                builder.ReleaseTemporaryRegistersToMarker(marker);
            }
            return;
        }

        if (argument.Kind == AstKind.Identifier)
        {
            var name = ast.GetString(argument.Arg0);
            if (TryResolveBinding(name, out var binding))
            {
                if (binding.Planned.StorageKind != CompilerPlannedStorageKind.GlobalBinding)
                {
                    builder.EmitLda(JsOpCode.LdaFalse);
                    return;
                }
            }
            else if (TryResolveExternalBinding(name, out _, out _))
            {
                builder.EmitLda(JsOpCode.LdaFalse);
                return;
            }
            else if (Vm.HasGlobalLexicalBindingAtom(Vm.Atoms.InternNoCheck(name)))
            {
                builder.EmitLda(JsOpCode.LdaFalse);
                return;
            }

            var marker = builder.GetTemporaryRegisterScopeMarker();
            try
            {
                var registers = builder.AllocateTemporaryRegisterBlock(2);
                EmitGlobalAccess("globalThis", JsOpCode.LdaGlobal, JsOpCode.LdaGlobalWide);
                EmitStar(registers);
                EmitStringLiteral(name);
                EmitStar(registers + 1);
                builder.EmitCallRuntime((int)RuntimeId.DeleteKeyedProperty, registers, 2);
            }
            finally
            {
                builder.ReleaseTemporaryRegistersToMarker(marker);
            }
            return;
        }

        EmitExpression(ast, argumentIndex);
        builder.EmitLda(JsOpCode.LdaTrue);
    }

    private void EmitDeleteOptionalChain(FlatAst ast, int expressionIndex)
    {
        ref readonly var expression = ref ast[expressionIndex];
        if (expression.Kind != AstKind.MemberExpression)
        {
            EmitOptionalChainExpression(ast, expressionIndex);
            builder.EmitLda(JsOpCode.LdaTrue);
            return;
        }

        var marker = builder.GetTemporaryRegisterScopeMarker();
        var previous = optionalChainNullTarget;
        var nullTarget = builder.CreateLabel();
        var done = builder.CreateLabel();
        optionalChainNullTarget = nullTarget;
        try
        {
            EmitExpression(ast, expression.Arg0);
            var registers = builder.AllocateTemporaryRegisterBlock(2);
            EmitStar(registers);
            if (((AstMemberFlags)expression.Arg2 & AstMemberFlags.OptionalChainLink) != 0)
                EmitOptionalChainNullCheck(registers);
            if (((AstMemberFlags)expression.Arg2 & AstMemberFlags.Computed) != 0)
                EmitExpression(ast, expression.Arg1);
            else
                EmitStringLiteral(ast.GetString(expression.Arg1));
            EmitStar(registers + 1);
            builder.EmitCallRuntime(
                (int)(
                    strictDeclared
                        ? RuntimeId.DeleteKeyedPropertyStrict
                        : RuntimeId.DeleteKeyedProperty
                ),
                registers,
                2
            );
            EmitJump(done);
            builder.BindLabel(nullTarget);
            builder.EmitLda(JsOpCode.LdaTrue);
            builder.BindLabel(done);
        }
        finally
        {
            optionalChainNullTarget = previous;
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitConditionalExpression(FlatAst ast, AstNode node, ExpressionResult result)
    {
        var alternate = builder.CreateLabel();
        var end = builder.CreateLabel();
        EmitExpressionForTest(ast, node.Arg0, alternate, jumpIfTrue: false);
        EmitExpression(ast, node.Arg1, result);
        EmitJump(end);
        builder.BindLabel(alternate);
        EmitExpression(ast, node.Arg2, result);
        builder.BindLabel(end);
    }

    private void EmitUpdateExpression(FlatAst ast, AstNode node)
    {
        ref readonly var argument = ref ast[node.Arg0];
        if (argument.Kind == AstKind.MemberExpression)
        {
            EmitMemberUpdate(ast, argument, node);
            return;
        }
        if (argument.Kind != AstKind.Identifier)
            throw new NotSupportedException(
                $"{CompilerName} supports only identifier update targets."
            );

        var name = ast.GetString(argument.Arg0);
        var hasLocalBinding = TryResolveBindingAccess(name, out var binding, out var contextDepth);
        var hasExternalBinding = TryResolveExternalBinding(
            name,
            out var externalBinding,
            out var externalDepth
        );

        EmitIdentifierLoad(name);
        var oldValueRegister = node.Arg2 == 0 ? builder.AllocateTemporaryRegister() : -1;
        try
        {
            if (oldValueRegister >= 0)
                EmitStar(oldValueRegister);
            builder.Emit(
                (JsUpdateOperator)node.Arg1 == JsUpdateOperator.Increment
                    ? JsOpCode.Inc
                    : JsOpCode.Dec
            );
            EmitResolvedIdentifierStore(
                name,
                hasLocalBinding,
                hasExternalBinding,
                binding,
                contextDepth,
                externalBinding,
                externalDepth
            );
            if (oldValueRegister >= 0)
                EmitLdar(oldValueRegister);
        }
        finally
        {
            if (oldValueRegister >= 0)
                builder.ReleaseTemporaryRegister(oldValueRegister);
        }
    }

    private void EmitMemberAssignment(
        FlatAst ast,
        AstNode member,
        JsAssignmentOperator op,
        int right
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var reference = PrepareMemberReference(
                ast,
                member,
                normalizeComputedKey: op != JsAssignmentOperator.Assign
            );
            if (op == JsAssignmentOperator.Assign)
                EmitExpression(ast, right);
            else if (IsLogicalAssignment(op))
            {
                EmitPreparedMemberLoad(reference);
                var end = builder.CreateLabel();
                EmitLogicalAssignmentShortCircuit(op, end);
                EmitExpression(ast, right);
                EmitPreparedMemberStore(reference);
                builder.BindLabel(end);
                return;
            }
            else
            {
                EmitPreparedMemberLoad(reference);
                EmitCompoundRightExpression(ast, op, right);
            }
            EmitPreparedMemberStore(reference);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private static bool IsLogicalAssignment(JsAssignmentOperator op)
    {
        return op
            is JsAssignmentOperator.LogicalAndAssign
                or JsAssignmentOperator.LogicalOrAssign
                or JsAssignmentOperator.NullishCoalescingAssign;
    }

    private void EmitMemberUpdate(FlatAst ast, AstNode member, AstNode update)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var reference = PrepareMemberReference(ast, member, normalizeComputedKey: true);
            EmitPreparedMemberLoad(reference);
            var oldValueRegister = update.Arg2 == 0 ? builder.AllocateTemporaryRegister() : -1;
            if (oldValueRegister >= 0)
                EmitStar(oldValueRegister);
            builder.Emit(
                (JsUpdateOperator)update.Arg1 == JsUpdateOperator.Increment
                    ? JsOpCode.Inc
                    : JsOpCode.Dec
            );
            EmitPreparedMemberStore(reference);
            if (oldValueRegister >= 0)
                EmitLdar(oldValueRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private PreparedMemberReference PrepareMemberReference(
        FlatAst ast,
        AstNode member,
        bool normalizeComputedKey
    )
    {
        if (ast[member.Arg0].Kind == AstKind.SuperExpression)
        {
            builder.EmitLda(JsOpCode.LdaThis);
            var receiverRegister = builder.AllocateTemporaryRegister();
            EmitStar(receiverRegister);
            if (((AstMemberFlags)member.Arg2 & AstMemberFlags.Computed) != 0)
                EmitExpression(ast, member.Arg1);
            else
                EmitStringLiteral(ast.GetString(member.Arg1));
            var superKeyRegister = builder.AllocateTemporaryRegister();
            EmitStar(superKeyRegister);
            if (normalizeComputedKey)
            {
                builder.EmitCallRuntime((int)RuntimeId.NormalizePropertyKey, superKeyRegister, 1);
                EmitStar(superKeyRegister);
            }
            return new(receiverRegister, superKeyRegister, -1, -1, true, true);
        }
        EmitExpression(ast, member.Arg0);
        var objectRegister = builder.AllocateTemporaryRegister();
        EmitStar(objectRegister);
        if (((AstMemberFlags)member.Arg2 & AstMemberFlags.Private) != 0)
        {
            var binding = ResolvePrivateBinding(ast.GetString(member.Arg1));
            return new(
                objectRegister,
                -1,
                -1,
                -1,
                false,
                false,
                binding.BrandId,
                binding.SlotIndex
            );
        }
        if (((AstMemberFlags)member.Arg2 & AstMemberFlags.Computed) != 0)
        {
            EmitExpression(ast, member.Arg1);
            var keyRegister = builder.AllocateTemporaryRegister();
            EmitStar(keyRegister);
            if (normalizeComputedKey)
            {
                builder.EmitCallRuntime((byte)RuntimeId.NormalizePropertyKey, keyRegister, 1);
                EmitStar(keyRegister);
            }
            return new(objectRegister, keyRegister, -1, -1, true, false);
        }

        var nameIndex = builder.AddAtomizedStringConstant(ast.GetString(member.Arg1));
        return new(objectRegister, -1, nameIndex, builder.AllocateFeedbackSlot(), false, false);
    }

    private void EmitPreparedMemberLoad(in PreparedMemberReference reference)
    {
        if (reference.PrivateBrandId >= 0)
        {
            EmitPrivateFieldOp(
                JsOpCode.GetPrivateField,
                reference.ObjectRegister,
                new(reference.PrivateBrandId, reference.PrivateSlotIndex)
            );
            return;
        }
        if (reference.IsSuper)
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(2);
            EmitLdar(reference.ObjectRegister);
            EmitStar(arguments);
            EmitLdar(reference.KeyRegister);
            EmitStar(arguments + 1);
            builder.EmitCallRuntime((int)RuntimeId.LoadKeyedFromSuper, arguments, 2);
            return;
        }
        if (reference.IsComputed)
        {
            EmitLdar(reference.KeyRegister);
            builder.EmitLdaKeyedProperty(reference.ObjectRegister);
        }
        else
            builder.EmitLdaNamedProperty(
                reference.ObjectRegister,
                reference.NameIndex,
                reference.FeedbackSlot
            );
    }

    private void EmitPreparedMemberStore(in PreparedMemberReference reference)
    {
        if (reference.PrivateBrandId >= 0)
        {
            var valueRegister = builder.AllocateTemporaryRegister();
            EmitStar(valueRegister);
            EmitPrivateFieldOp(
                JsOpCode.SetPrivateField,
                reference.ObjectRegister,
                valueRegister,
                new(reference.PrivateBrandId, reference.PrivateSlotIndex)
            );
            return;
        }
        if (reference.IsSuper)
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(3);
            EmitStar(arguments + 2);
            EmitLdar(reference.ObjectRegister);
            EmitStar(arguments);
            EmitLdar(reference.KeyRegister);
            EmitStar(arguments + 1);
            builder.EmitCallRuntime((int)RuntimeId.SuperSet, arguments, 3);
            return;
        }
        if (reference.IsComputed)
            builder.EmitStaKeyedProperty(reference.ObjectRegister, reference.KeyRegister);
        else
            builder.EmitStaNamedProperty(
                reference.ObjectRegister,
                reference.NameIndex,
                reference.FeedbackSlot
            );
    }

    private readonly record struct PreparedMemberReference(
        int ObjectRegister,
        int KeyRegister,
        int NameIndex,
        int FeedbackSlot,
        bool IsComputed,
        bool IsSuper,
        int PrivateBrandId = -1,
        int PrivateSlotIndex = -1
    );

    private void EmitSequenceExpression(FlatAst ast, AstNode node, ExpressionResult result)
    {
        var expressions = ast.ChildRange(node.Arg0, node.Arg1);
        if (expressions.Length == 0)
        {
            if (result.Mode == ExpressionResultMode.Value)
                builder.EmitLda(JsOpCode.LdaUndefined);
            return;
        }

        for (var i = 0; i < expressions.Length - 1; i++)
            EmitExpressionForEffect(ast, expressions[i]);
        EmitExpression(ast, expressions[^1], result);
    }

    private void EmitIdentifierAssignment(
        FlatAst ast,
        string name,
        JsAssignmentOperator op,
        int right,
        bool inferName
    )
    {
        var hasLocalBinding = TryResolveBindingAccess(name, out var binding, out var contextDepth);
        var hasExternalBinding = TryResolveExternalBinding(
            name,
            out var externalBinding,
            out var externalDepth
        );

        switch (op)
        {
            case JsAssignmentOperator.Assign:
                if (inferName)
                    EmitExpressionWithInferredName(ast, right, name);
                else
                    EmitExpression(ast, right);
                EmitResolvedIdentifierStore(
                    name,
                    hasLocalBinding,
                    hasExternalBinding,
                    binding,
                    contextDepth,
                    externalBinding,
                    externalDepth
                );
                return;
            case JsAssignmentOperator.AddAssign:
            case JsAssignmentOperator.SubtractAssign:
            case JsAssignmentOperator.MultiplyAssign:
            case JsAssignmentOperator.ExponentiateAssign:
            case JsAssignmentOperator.DivideAssign:
            case JsAssignmentOperator.ModuloAssign:
            case JsAssignmentOperator.ShiftLeftAssign:
            case JsAssignmentOperator.ShiftRightAssign:
            case JsAssignmentOperator.ShiftRightLogicalAssign:
            case JsAssignmentOperator.BitwiseAndAssign:
            case JsAssignmentOperator.BitwiseOrAssign:
            case JsAssignmentOperator.BitwiseXorAssign:
                EmitIdentifierLoad(name);
                EmitCompoundRightExpression(ast, op, right);
                EmitResolvedIdentifierStore(
                    name,
                    hasLocalBinding,
                    hasExternalBinding,
                    binding,
                    contextDepth,
                    externalBinding,
                    externalDepth
                );
                return;
            case JsAssignmentOperator.LogicalAndAssign:
            case JsAssignmentOperator.LogicalOrAssign:
            case JsAssignmentOperator.NullishCoalescingAssign:
                EmitShortCircuitIdentifierAssignment(
                    ast,
                    name,
                    op,
                    right,
                    hasLocalBinding,
                    hasExternalBinding,
                    binding,
                    contextDepth,
                    externalBinding,
                    externalDepth
                );
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support assignment operator '{op}'."
                );
        }
    }

    private void EmitCompoundRightExpression(FlatAst ast, JsAssignmentOperator op, int right)
    {
        var binaryOp = MapCompoundAssignmentOperator(op);
        if (
            TryGetSmallIntLiteral(ast, right, out var rhsSmi)
            && TryMapSmiBinaryOpcode(binaryOp, out var smiOpcode)
        )
        {
            EmitImmediateWithSlotOp(smiOpcode, rhsSmi);
            return;
        }

        var lhsRegister = builder.AllocateTemporaryRegister();
        try
        {
            EmitStar(lhsRegister);
            EmitExpression(ast, right);
            if (!TryMapBinaryOpcode(binaryOp, out var opcode))
                throw new NotSupportedException(
                    $"{CompilerName} does not support assignment operator '{op}'."
                );
            EmitRegisterWithSlotOp(opcode, lhsRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegister(lhsRegister);
        }
    }

    private void EmitShortCircuitIdentifierAssignment(
        FlatAst ast,
        string name,
        JsAssignmentOperator op,
        int right,
        bool hasLocalBinding,
        bool hasExternalBinding,
        BindingStorage binding,
        int contextDepth,
        CapturedBindingAccess externalBinding,
        int externalDepth
    )
    {
        EmitIdentifierLoad(name);
        var end = builder.CreateLabel();
        EmitLogicalAssignmentShortCircuit(op, end);
        EmitExpression(ast, right);
        EmitResolvedIdentifierStore(
            name,
            hasLocalBinding,
            hasExternalBinding,
            binding,
            contextDepth,
            externalBinding,
            externalDepth
        );
        builder.BindLabel(end);
    }

    private void EmitLogicalAssignmentShortCircuit(
        JsAssignmentOperator op,
        BytecodeBuilder.Label end
    )
    {
        switch (op)
        {
            case JsAssignmentOperator.LogicalAndAssign:
                EmitJumpIfToBooleanFalse(end);
                return;
            case JsAssignmentOperator.LogicalOrAssign:
                EmitJumpIfToBooleanTrue(end);
                return;
            case JsAssignmentOperator.NullishCoalescingAssign:
                var assign = builder.CreateLabel();
                EmitJumpIfNull(assign);
                EmitJumpIfUndefined(assign);
                EmitJump(end);
                builder.BindLabel(assign);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(op));
        }
    }

    private static bool TryMapBinaryOpcode(JsBinaryOperator op, out JsOpCode opcode)
    {
        opcode = op switch
        {
            JsBinaryOperator.Add => JsOpCode.Add,
            JsBinaryOperator.Subtract => JsOpCode.Sub,
            JsBinaryOperator.Multiply => JsOpCode.Mul,
            JsBinaryOperator.Divide => JsOpCode.Div,
            JsBinaryOperator.Modulo => JsOpCode.Mod,
            JsBinaryOperator.Exponentiate => JsOpCode.Exp,
            JsBinaryOperator.BitwiseAnd => JsOpCode.BitwiseAnd,
            JsBinaryOperator.BitwiseOr => JsOpCode.BitwiseOr,
            JsBinaryOperator.BitwiseXor => JsOpCode.BitwiseXor,
            JsBinaryOperator.ShiftLeft => JsOpCode.ShiftLeft,
            JsBinaryOperator.ShiftRight => JsOpCode.ShiftRight,
            JsBinaryOperator.ShiftRightLogical => JsOpCode.ShiftRightLogical,
            JsBinaryOperator.Equal => JsOpCode.TestEqual,
            JsBinaryOperator.NotEqual => JsOpCode.TestNotEqual,
            JsBinaryOperator.StrictEqual => JsOpCode.TestEqualStrict,
            JsBinaryOperator.LessThan => JsOpCode.TestLessThan,
            JsBinaryOperator.GreaterThan => JsOpCode.TestGreaterThan,
            JsBinaryOperator.LessThanOrEqual => JsOpCode.TestLessThanOrEqual,
            JsBinaryOperator.GreaterThanOrEqual => JsOpCode.TestGreaterThanOrEqual,
            JsBinaryOperator.In => JsOpCode.TestIn,
            JsBinaryOperator.Instanceof => JsOpCode.TestInstanceOf,
            _ => default,
        };
        return opcode != default;
    }

    private static bool TryMapSmiBinaryOpcode(JsBinaryOperator op, out JsOpCode opcode)
    {
        opcode = op switch
        {
            JsBinaryOperator.Add => JsOpCode.AddSmi,
            JsBinaryOperator.Subtract => JsOpCode.SubSmi,
            JsBinaryOperator.Multiply => JsOpCode.MulSmi,
            JsBinaryOperator.Modulo => JsOpCode.ModSmi,
            JsBinaryOperator.Exponentiate => JsOpCode.ExpSmi,
            JsBinaryOperator.LessThan => JsOpCode.TestLessThanSmi,
            JsBinaryOperator.GreaterThan => JsOpCode.TestGreaterThanSmi,
            JsBinaryOperator.LessThanOrEqual => JsOpCode.TestLessThanOrEqualSmi,
            JsBinaryOperator.GreaterThanOrEqual => JsOpCode.TestGreaterThanOrEqualSmi,
            _ => default,
        };
        return opcode != default;
    }

    private static JsBinaryOperator MapCompoundAssignmentOperator(JsAssignmentOperator op)
    {
        return op switch
        {
            JsAssignmentOperator.AddAssign => JsBinaryOperator.Add,
            JsAssignmentOperator.SubtractAssign => JsBinaryOperator.Subtract,
            JsAssignmentOperator.MultiplyAssign => JsBinaryOperator.Multiply,
            JsAssignmentOperator.ExponentiateAssign => JsBinaryOperator.Exponentiate,
            JsAssignmentOperator.DivideAssign => JsBinaryOperator.Divide,
            JsAssignmentOperator.ModuloAssign => JsBinaryOperator.Modulo,
            JsAssignmentOperator.ShiftLeftAssign => JsBinaryOperator.ShiftLeft,
            JsAssignmentOperator.ShiftRightAssign => JsBinaryOperator.ShiftRight,
            JsAssignmentOperator.ShiftRightLogicalAssign => JsBinaryOperator.ShiftRightLogical,
            JsAssignmentOperator.BitwiseAndAssign => JsBinaryOperator.BitwiseAnd,
            JsAssignmentOperator.BitwiseOrAssign => JsBinaryOperator.BitwiseOr,
            JsAssignmentOperator.BitwiseXorAssign => JsBinaryOperator.BitwiseXor,
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
        };
    }

    private void EmitNumericLiteral(double number)
    {
        if (Math.Truncate(number) == number && number >= int.MinValue && number <= int.MaxValue)
        {
            EmitSmi((int)number);
            return;
        }

        EmitNumericConstant(number);
    }

    protected void EmitStringLiteral(string value)
    {
        EmitStringConstant(builder.AddObjectConstant(value));
    }

    private static bool TryGetSmallIntLiteral(FlatAst ast, int nodeIndex, out int value)
    {
        ref readonly var node = ref ast[nodeIndex];
        if (node.Kind == AstKind.NumericLiteral)
        {
            var number = ast.GetNumber(node.Arg0);
            if (
                Math.Truncate(number) == number
                && number >= sbyte.MinValue
                && number <= sbyte.MaxValue
            )
            {
                value = (int)number;
                return true;
            }
        }

        value = default;
        return false;
    }

    private void EmitIdentifierLoad(string name)
    {
        if (!TryResolveBindingAccess(name, out var binding, out var contextDepth))
        {
            if (string.Equals(name, "undefined", StringComparison.Ordinal))
            {
                builder.EmitLda(JsOpCode.LdaUndefined);
                return;
            }

            if (TryResolveExternalBinding(name, out var externalBinding, out var externalDepth))
            {
                if (externalBinding.IsModuleVariable)
                    EmitModuleVariableAccess(
                        JsOpCode.LdaModuleVariable,
                        externalBinding.Slot,
                        externalDepth
                    );
                else
                    EmitLdaContextSlot(externalBinding.Slot, externalDepth);
                return;
            }

            EmitGlobalAccess(name, JsOpCode.LdaGlobal, JsOpCode.LdaGlobalWide);
            return;
        }

        switch (binding.Planned.StorageKind)
        {
            case CompilerPlannedStorageKind.LocalRegister:
                EmitLdar(binding.Register);
                return;
            case CompilerPlannedStorageKind.LexicalRegister:
                EmitLdaLexicalLocal(binding.Register);
                return;
            case CompilerPlannedStorageKind.ContextSlot:
                if (contextDepth == 0)
                    EmitLdaCurrentContextSlot(binding.Planned.StorageIndex);
                else
                    EmitLdaContextSlot(binding.Planned.StorageIndex, contextDepth);
                return;
            case CompilerPlannedStorageKind.GlobalBinding:
                EmitGlobalAccess(name, JsOpCode.LdaGlobal, JsOpCode.LdaGlobalWide);
                return;
            case CompilerPlannedStorageKind.ModuleBinding:
                EmitModuleVariableAccess(
                    JsOpCode.LdaModuleVariable,
                    binding.Planned.StorageIndex,
                    contextDepth
                );
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support loading '{name}' from {binding.Planned.StorageKind}."
                );
        }
    }

    protected void EmitStore(
        BindingStorage binding,
        bool isInitialization = false,
        bool isFunctionDeclaration = false
    )
    {
        EmitStore(binding, 0, isInitialization, isFunctionDeclaration);
    }

    private void EmitStore(
        BindingStorage binding,
        int contextDepth,
        bool isInitialization = false,
        bool isFunctionDeclaration = false
    )
    {
        if (!isInitialization && binding.Planned.IsConst)
        {
            EmitThrowConstAssignError(binding.Planned.Name);
            return;
        }
        if (
            !isInitialization
            && binding.Planned.Kind == CompilerCollectedBindingKind.FunctionNameSelf
        )
        {
            if (strictDeclared)
                EmitThrowConstAssignError(binding.Planned.Name);
            return;
        }

        switch (binding.Planned.StorageKind)
        {
            case CompilerPlannedStorageKind.LocalRegister:
                EmitStar(binding.Register);
                return;
            case CompilerPlannedStorageKind.LexicalRegister:
                if (isInitialization)
                    EmitStar(binding.Register);
                else
                    EmitStaLexicalLocal(binding.Register);
                return;
            case CompilerPlannedStorageKind.ContextSlot:
                if (contextDepth == 0)
                    EmitStaCurrentContextSlot(binding.Planned.StorageIndex);
                else
                    EmitStaContextSlot(binding.Planned.StorageIndex, contextDepth);
                return;
            case CompilerPlannedStorageKind.GlobalBinding:
                EmitGlobalAccess(
                    binding.Planned.Name,
                    isFunctionDeclaration ? JsOpCode.StaGlobalFuncDecl
                        : isInitialization ? JsOpCode.StaGlobalInit
                        : JsOpCode.StaGlobal,
                    isFunctionDeclaration ? JsOpCode.StaGlobalFuncDeclWide
                        : isInitialization ? JsOpCode.StaGlobalInitWide
                        : JsOpCode.StaGlobalWide
                );
                return;
            case CompilerPlannedStorageKind.ModuleBinding:
                EmitModuleVariableAccess(
                    JsOpCode.StaModuleVariable,
                    binding.Planned.StorageIndex,
                    contextDepth
                );
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support storing '{binding.Planned.Name}' in {binding.Planned.StorageKind}."
                );
        }
    }

    private void EmitResolvedIdentifierStore(
        string name,
        bool hasLocalBinding,
        bool hasExternalBinding,
        BindingStorage binding,
        int contextDepth,
        CapturedBindingAccess externalBinding,
        int externalDepth
    )
    {
        if (hasLocalBinding)
        {
            EmitStore(binding, contextDepth);
            return;
        }

        if (hasExternalBinding)
        {
            if (externalBinding.IsConst)
            {
                EmitThrowConstAssignError(name);
                return;
            }
            if (externalBinding.IsImmutableFunctionName)
            {
                if (strictDeclared)
                    EmitThrowConstAssignError(name);
                return;
            }
            if (externalBinding.IsModuleVariable)
                EmitModuleVariableAccess(
                    JsOpCode.StaModuleVariable,
                    externalBinding.Slot,
                    externalDepth
                );
            else
                EmitStaContextSlot(externalBinding.Slot, externalDepth);
            return;
        }

        EmitGlobalAccess(name, JsOpCode.StaGlobal, JsOpCode.StaGlobalWide);
    }

    private void EmitThrowConstAssignError(string name)
    {
        var callPc = builder.CodeLength;
        builder.EmitCallRuntime((int)RuntimeId.ThrowConstAssignError, 0, 0);
        builder.AddRuntimeCallDebugName(callPc, name);
    }
}
