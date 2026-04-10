using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlSegmenterTests
{
    [Test]
    public void Intl_Segmenter_Constructor_And_ResolvedOptions_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const ctorDesc = Object.getOwnPropertyDescriptor(Intl, "Segmenter");
                                const supportedDesc = Object.getOwnPropertyDescriptor(Intl.Segmenter, "supportedLocalesOf");
                                const tagDesc = Object.getOwnPropertyDescriptor(Intl.Segmenter.prototype, Symbol.toStringTag);
                                const segmenter = new Intl.Segmenter("fr-CA", { granularity: "word" });
                                const options = segmenter.resolvedOptions();
                                [
                                  typeof Intl.Segmenter === "function",
                                  ctorDesc.writable,
                                  ctorDesc.enumerable,
                                  ctorDesc.configurable,
                                  Intl.Segmenter.length,
                                  typeof Intl.Segmenter.supportedLocalesOf === "function",
                                  supportedDesc.enumerable,
                                  supportedDesc.configurable,
                                  Object.prototype.toString.call(segmenter),
                                  options.locale,
                                  options.granularity,
                                  tagDesc.value,
                                  Intl.Segmenter.supportedLocalesOf(["fr-CA"]).join("|")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "true|true|false|true|0|true|false|true|[object Intl.Segmenter]|fr-CA|word|Intl.Segmenter|fr-CA"));
    }

    [Test]
    public void Intl_Segmenter_Segments_Containing_And_Iteration_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const segments = new Intl.Segmenter("en", { granularity: "word" }).segment("Hello, world!");
                                const seen = [];
                                for (const part of segments) {
                                  seen.push(part.segment + ":" + part.index + ":" + String(part.isWordLike));
                                }
                                const mid = segments.containing(7);
                                [
                                  typeof segments.containing === "function",
                                  seen.join("/"),
                                  mid.segment,
                                  mid.index,
                                  String(mid.isWordLike),
                                  segments.containing(-1) === undefined,
                                  segments.containing(99) === undefined
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "true|Hello:0:true/,:5:false/ :6:false/world:7:true/!:12:false|world|7|true|true|true"));
    }

    [Test]
    public void Intl_Segmenter_Options_And_SupportedLocalesOf_Validate_Inputs()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const marker = new Error("marker");
                                let abrupt = false;
                                try {
                                  new Intl.Segmenter("en", {
                                    get granularity() {
                                      return {
                                        toString() {
                                          throw marker;
                                        }
                                      };
                                    }
                                  });
                                } catch (e) {
                                  abrupt = e === marker;
                                }

                                let nullThrows = false;
                                try {
                                  Intl.Segmenter.supportedLocalesOf(null);
                                } catch (e) {
                                  nullThrows = e && e.name === "TypeError";
                                }

                                abrupt && nullThrows;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
