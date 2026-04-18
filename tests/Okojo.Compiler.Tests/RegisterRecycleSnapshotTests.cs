using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Compiler.Tests;

public class RegisterRecycleSnapshotTests
{
    private static readonly (string Name, string Source, int MaxScriptRegisters)[] ScopeCases =
    [
        (
            "block_let_tdz",
            """
            let outer = 10;
            {
              let x = outer + 1;
              const y = x + 1;
              outer = y;
            }
            outer;
            """,
            5
        ),
        (
            "for_loop_let_capture",
            """
            let arr = [];
            for (let i = 0; i < 3; i++) {
              arr.push(function() { return i; });
            }
            arr[0]() + arr[1]() + arr[2]();
            """,
            8
        ),
        (
            "try_catch_binding",
            """
            let out = 0;
            try {
              throw 41;
            } catch (e) {
              let local = e + 1;
              out = local;
            }
            out;
            """,
            7
        ),
        (
            "nested_closure_scope",
            """
            function makeAdder(a) {
              let base = a;
              return function inner(b) {
                let c = b;
                return base + c;
              };
            }
            let add2 = makeAdder(2);
            add2(5);
            """,
            3
        ),
        (
            "class_scope_name",
            """
            let C = class Named {
              static n() { return Named; }
              getName() { return Named; }
            };
            (C.n() === C) && (new C().getName() === C);
            """,
            7
        ),
        (
            "computed_key_scope_side_effect",
            """
            let counter = 0;
            let key = {
              toString: function() {
                counter = counter + 1;
                return "k" + counter;
              }
            };
            let obj = {
              [key]: 1,
              [key]: 2,
            };
            (counter === 2) && (obj.k1 === 1) && (obj.k2 === 2);
            """,
            6
        )
    ];

    [Test]
    public void ScopeCorpus_ScriptRegisterCount_StaysWithinCeiling()
    {
        foreach (var item in ScopeCases)
        {
            var realm = JsRuntime.Create().DefaultRealm;
            var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript(item.Source));

            Assert.That(
                script.RegisterCount,
                Is.LessThanOrEqualTo(item.MaxScriptRegisters),
                $"{item.Name} exceeded register ceiling (actual={script.RegisterCount}, max={item.MaxScriptRegisters}).");
        }
    }

    [Test]
    public void SiblingBlockLexicals_FunctionRegisterCount_ReusesRegisters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                     {
                                                                       let x0 = 0, x1 = 1, x2 = 2, x3 = 3, x4 = 4, x5 = 5, x6 = 6, x7 = 7, x8 = 8, x9 = 9;
                                                                       let x10 = 10, x11 = 11, x12 = 12, x13 = 13, x14 = 14, x15 = 15, x16 = 16, x17 = 17, x18 = 18, x19 = 19;
                                                                       let x20 = 20, x21 = 21, x22 = 22, x23 = 23, x24 = 24, x25 = 25, x26 = 26, x27 = 27, x28 = 28, x29 = 29;
                                                                       let x30 = 30, x31 = 31, x32 = 32, x33 = 33, x34 = 34, x35 = 35, x36 = 36, x37 = 37, x38 = 38, x39 = 39;
                                                                       x0 + x1 + x2 + x3 + x4 + x5 + x6 + x7 + x8 + x9 + x10 + x11 + x12 + x13 + x14 + x15 + x16 + x17 + x18 + x19 + x20 + x21 + x22 + x23 + x24 + x25 + x26 + x27 + x28 + x29 + x30 + x31 + x32 + x33 + x34 + x35 + x36 + x37 + x38 + x39;
                                                                     }
                                                                     {
                                                                       let y0 = 0, y1 = 1, y2 = 2, y3 = 3, y4 = 4, y5 = 5, y6 = 6, y7 = 7, y8 = 8, y9 = 9;
                                                                       let y10 = 10, y11 = 11, y12 = 12, y13 = 13, y14 = 14, y15 = 15, y16 = 16, y17 = 17, y18 = 18, y19 = 19;
                                                                       let y20 = 20, y21 = 21, y22 = 22, y23 = 23, y24 = 24, y25 = 25, y26 = 26, y27 = 27, y28 = 28, y29 = 29;
                                                                       let y30 = 30, y31 = 31, y32 = 32, y33 = 33, y34 = 34, y35 = 35, y36 = 36, y37 = 37, y38 = 38, y39 = 39;
                                                                       return y0 + y1 + y2 + y3 + y4 + y5 + y6 + y7 + y8 + y9 + y10 + y11 + y12 + y13 + y14 + y15 + y16 + y17 + y18 + y19 + y20 + y21 + y22 + y23 + y24 + y25 + y26 + y27 + y28 + y29 + y30 + y31 + y32 + y33 + y34 + y35 + y36 + y37 + y38 + y39;
                                                                     }
                                                                   }
                                                                   """));

        var function = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(static fn => fn.Name == "t");
        Assert.That(function.Script.RegisterCount, Is.LessThanOrEqualTo(45));
    }
}
