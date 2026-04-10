using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private void EmitTemplateStringExpression(JsTemplateExpression templateExpr)
    {
        if (templateExpr.Quasis.Count == 0)
        {
            var emptyIdx = builder.AddObjectConstant(string.Empty);
            EmitLdaStringConstantByIndex(emptyIdx);
            return;
        }

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var firstIdx = builder.AddObjectConstant(templateExpr.Quasis[0] ?? string.Empty);
            EmitLdaStringConstantByIndex(firstIdx);
            var lhsReg = AllocateTemporaryRegister();

            for (var i = 0; i < templateExpr.Expressions.Count; i++)
            {
                EmitStarRegister(lhsReg);
                VisitExpression(templateExpr.Expressions[i]);
                EmitRaw(JsOpCode.ToString);
                EmitRegisterSlotOp(JsOpCode.Add, lhsReg);

                var quasiIdx = builder.AddObjectConstant(templateExpr.Quasis[i + 1] ?? string.Empty);
                EmitStarRegister(lhsReg);
                EmitLdaStringConstantByIndex(quasiIdx);
                EmitRegisterSlotOp(JsOpCode.Add, lhsReg);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitTaggedTemplateCallExpression(JsTaggedTemplateExpression taggedTemplate)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argCount = 1 + taggedTemplate.Template.Expressions.Count;
            var argStart = AllocateTemporaryRegisterBlock(argCount);
            EmitTaggedTemplateObjectIntoRegister(taggedTemplate.Template, argStart);
            for (var i = 0; i < taggedTemplate.Template.Expressions.Count; i++)
            {
                VisitExpression(taggedTemplate.Template.Expressions[i]);
                EmitStarRegister(argStart + 1 + i);
            }

            if (taggedTemplate.Tag is JsMemberExpression memberTag)
            {
                int objReg;
                if (!TryGetPlainLocalReadRegister(memberTag.Object, out objReg))
                {
                    VisitExpression(memberTag.Object);
                    objReg = AllocateTemporaryRegister();
                    EmitStarRegister(objReg);
                }

                if (memberTag.IsPrivate)
                {
                    if (!TryResolvePrivateMemberBinding(memberTag, out var privateBinding))
                        throw new NotSupportedException(
                            "Private tagged template call shape is not supported in Okojo Phase 2.");
                    EmitPrivateFieldOp(JsOpCode.GetPrivateField, objReg, privateBinding.BrandId,
                        privateBinding.SlotIndex);
                }
                else if (memberTag.IsComputed)
                {
                    VisitExpression(memberTag.Property);
                    EmitLdaKeyedProperty(objReg);
                }
                else
                {
                    if (!TryGetNamedMemberKey(memberTag, out var memberName))
                        throw new NotImplementedException(
                            "Only non-private member tagged template calls are supported in Okojo Phase 1.");
                    var nameIdx = builder.AddAtomizedStringConstant(memberName);
                    var feedbackSlot = builder.AllocateFeedbackSlot();
                    EmitLdaNamedPropertyByIndex(objReg, nameIdx, feedbackSlot);
                }

                var funcReg = AllocateTemporaryRegister();
                EmitStarRegister(funcReg);
                EmitCallProperty(funcReg, objReg, argStart, argCount);
                return;
            }

            VisitExpression(taggedTemplate.Tag);
            var tagFuncReg = AllocateTemporaryRegister();
            EmitStarRegister(tagFuncReg);
            EmitCallUndefinedReceiver(tagFuncReg, argStart, argCount);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitTaggedTemplateObjectIntoRegister(JsTemplateExpression template, int targetReg)
    {
        var cooked = template.Quasis.ToArray();
        var raw = template.RawQuasis.ToArray();
        var descriptorIdx = builder.AddObjectConstant(new JsTemplateSiteDescriptor(cooked, raw));
        EmitLda(descriptorIdx);
        EmitStarRegister(targetReg);
        EmitCallRuntime(RuntimeId.GetTemplateObject, targetReg, 1);
        EmitStarRegister(targetReg);
    }
}
