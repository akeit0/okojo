import "nuget:Newtonsoft.Json@13.0.3";
const Newtonsoft = clr.Newtonsoft;
const JsonConvert = Newtonsoft.Json.JsonConvert;
const JToken = Newtonsoft.Json.Linq.JToken;

const payload = JToken.Parse(`{
    "demo":"node-nuget-import",
    "engine":"okojo",
    "items":[1,2,3]
    }`);
console.log(payload.get_Item("demo").Value);
const text = JsonConvert.SerializeObject(payload);
console.log(text);
